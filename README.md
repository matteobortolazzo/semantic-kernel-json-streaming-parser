# Incremental JSON parsing with Utf8JsonReader

## The problem

[LLMs](https://en.wikipedia.org/wiki/Large_language_model) like [GPT-4o](https://openai.com/index/hello-gpt-4o/) are now
good at generating `JSON`, which opens up many possibilities.

Most of the time we can just wait for the *LLM* to complete the generation, parse the answer and return to the *UI*.

However, given the speed of *LLMs*, it can be frustrating for users to wait for the completion of the generation.

The best solution would be to display the generated content incrementally as soon as possible. This is quite easy with
text, but it's a bit more complicated with `JSON` as we need to make sure that the content is valid at each step.
We need to parse the `JSON` while it's being generated, understand the structure, and act accordingly.

As an example, we'll build a **Life’s to-do list** generator. The *LLM* will generate a list of tasks, and we'll display
them as soon as they are generated.

We'll make it more complex with the following schema:

```json
{
  "listName": "Bucket List",
  "items": [
    {
      "recommendedAge": 30,
      "description": "Skydiving"
    },
    {
      "recommendedAge": 50,
      "description": "Visit all seven continents"
    }
  ]
}
```

![TODO list](./imgs/output.gif?raw=true "TODO list")

There are two ready-to-use tools we can use:

- [Semantic Kernel](https://learn.microsoft.com/en-us/semantic-kernel/overview/): an *SDK* to interact with AI models.
- [Utf8JsonReader](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonreader):
  a high-performance, low-allocation, forward-only reader for `JSON`.

It should be straightforward to combine these two tools to achieve our goal, there's even a section in the
docs: [Read from a stream using Utf8JsonReader](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonreader#read-from-a-stream-using-utf8jsonreader)!

## The actual problem

Actually, there are multiple challenges:

- The reader example uses a `MemoryStream` while Semantic Kernel uses `IAsyncEnumerable<StreamingTextContent>`.
- `Utf8JsonReader` is a `ref struct`, so:
    - It doesn't work with streams anyway, only with `ReadOnlySpan<byte>` in the constructor.
    - It can't be passed a parameter to an `async` method.
    - It can't be used across `await` or `yield` boundaries.
    - It's a [lexer/tokenizer](https://en.wikipedia.org/wiki/Lexical_analysis), not a parser, so we need to handle the
      `JSON` structure ourselves.

## The solution

We need to solve two problems:

- How to use `Utf8JsonReader` with `IAsyncEnumerable<StreamingTextContent>`.
- How to parse the `JSON` structure incrementally.

Let's start with the latter as it's simpler.

## The parser

The main method of `Utf8JsonReader` is `Read()`. A simple `JSON` like `{ "name": "test" }` will return generate the
following tokens:

- `StartObject`
- `PropertyName`
- `String`
- `EndObject`

Each time we call `Read()`, the reader move forward and we use:

- `TokenType` to know the type of the token.
- `ValueSpan`, and other methods, to get the value of the token.
- The `bool` returned to know if there are more tokens to read.

The interface for this is quite simple:

```csharp
public interface IIncrementalJsonStreamParser
{
    void ContinueParsing(ref Utf8JsonReader reader);
}
```

Once we buffered enough data from the response, we try to parse it.

### State machine

The easiest way I found to parse the `JSON` with this setup is a state machine.
With each token, we update the state of the machine and act accordingly, for example, by triggering an event.

Here the state machine for the `TODO` list:

![TODO list](./imgs/state-machine.png?raw=true "TODO")

### Visitor pattern

To hide the complexity of `Uft8JsonReader`, we can use the visitor pattern.

The base `abstract` base looks something like this:

```csharp
private T State { get; set; } = initialState;

public void ContinueParsing(ref Utf8JsonReader reader)
{
    while (reader.Read())
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.PropertyName:
                State = VisitProperty(State, reader.GetString()!);
                break;
            case JsonTokenType.String:
                State = VisitStringValue(State, reader.GetString()!);
                break;
            
            // etc.
        }
    }
}    

protected virtual T VisitProperty(T state, string propertyName) => state;

protected virtual T VisitStringValue(T state, string value) => state;

// etc.
```

We can then implement only what we need, changing the state and triggering events:
```csharp
public enum TodoStateMachineState
{
    None,
    ReadingListName
    // etc.
}


public class TodoStateMachineJsonTokenParser(
    Action<string> onListNameParsed) : StateMachineJsonTokenParser<TodoStateMachineState>(TodoStateMachineState.None)
{
    protected override TodoStateMachineState VisitProperty(TodoStateMachineState stateMachineState, string propertyName)
    {
        return propertyName switch
        {
            "listName" => TodoStateMachineState.ReadingListName,
            // etc.
        };
    }

    protected override TodoStateMachineState VisitStringValue(TodoStateMachineState stateMachineState, string value)
    {
        switch (stateMachineState)
        {
            case TodoStateMachineState.ReadingListName:
                onListNameParsed(value); // Trigger event
                return TodoStateMachineState.None;
            // etc.
        }
    }
    
    // etc.
}
```
## The feeder

Let's now see how we can keep feeding the parser. Below the full implementation:

```csharp
/// <summary>
/// Provides a way to feed an <see cref="IIncrementalJsonStreamParser"/> with an <see cref="IAsyncEnumerable{T}"/> of <see cref="StreamingTextContent"/>
/// </summary>
/// <param name="incrementalParser">The parser.</param>
/// <param name="chunkBufferSize">The number of chunks to read before feeding the parser.</param>
public class JsonAsyncStreamTokenFeeder(IIncrementalJsonStreamParser incrementalParser, int chunkBufferSize)
{
    /// <summary>
    /// Start feeding the parser with the text content stream
    /// </summary>
    /// <param name="textContentStream">The source.</param>
    public async Task FeedAsync(IAsyncEnumerable<StreamingTextContent> textContentStream)
    {
        // Control the pace of the stream by reading in chunks
        var e = textContentStream.GetAsyncEnumerator();

        var completed = false;
        var buffer = new ArrayBufferWriter<byte>();

        try
        {
            JsonReaderState jsonReaderState = new();
            while (!completed)
            {
                // Load the buffer with the next chunk of text
                for (var i = 0; i < chunkBufferSize; i++)
                {
                    var readSuccess = await e.MoveNextAsync();
                    // Reached the end of the stream
                    if (!readSuccess)
                    {
                        completed = true;
                        break;
                    }
                    
                    if (e.Current.Text == null) continue;
                    var bytes = Encoding.UTF8.GetBytes(e.Current.Text);
                    buffer.Write(bytes);
                }

                // Load the reader with the buffer
                var reader = new Utf8JsonReader(
                    buffer.WrittenSpan,
                    isFinalBlock: false,
                    state: jsonReaderState);

                // Parse a much as possible
                incrementalParser.ContinueParsing(ref reader);

                // Save the parsing state
                jsonReaderState = reader.CurrentState;

                // Create a new buffer with the leftover bytes that were not consumed by the parser
                // This happens when the parser is in the middle of a token
                var remainingBytes = buffer.WrittenSpan[(int)reader.BytesConsumed..];
                buffer = new ArrayBufferWriter<byte>();
                buffer.Write(remainingBytes);
            }
        }
        finally
        {
            await e.DisposeAsync();
        }
    }
}
```

Explanation:

- We manually load a given number of chunks into a buffer.
- We create a `Utf8JsonReader` with the buffer. 
  - `IsFinalBlock` is `false` as we don't know if we have reached the end of the stream.
  - We pass the `JsonReaderState` to keep track of the parsing state.
- Call `ContinueParsing` on the parser. The parser returns once there are no more tokens to read.
- We save the state of the reader.
- We create a new buffer with the remaining bytes that were not consumed by the parser.
- We start again until we reach the end of the stream.

## Usage

Here's an example of the usage of everything we created:
```csharp
OpenAIPromptExecutionSettings openAiPromptExecutionSettings = new()
{
    ResponseFormat = "json_object",
};
var textContentStream = textGenerationService
    .GetStreamingTextContentsAsync(prompt, openAiPromptExecutionSettings);

var parser = new TodoStateMachineJsonTokenParser(PrintName, PrintItem); // Callbacks
var feeder = new JsonAsyncStreamTokenFeeder(parser, chunkBufferSize: 32);
await feeder.FeedAsync(textContentStream);
```