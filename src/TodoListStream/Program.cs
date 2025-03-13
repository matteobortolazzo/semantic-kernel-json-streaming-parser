using AsyncEnumerableJsonParser;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.TextGeneration;
using TodoListGenerator;

var builder = Kernel.CreateBuilder();
var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
builder.Services.AddOpenAIChatCompletion(
    modelId: "gpt-4o",
    apiKey: apiKey);

var kernel = builder.Build();
var textGenerationService = kernel.GetRequiredService<ITextGenerationService>();

OpenAIPromptExecutionSettings openAiPromptExecutionSettings = new()
{
#pragma warning disable SKEXP0010
    ResponseFormat = "json_object",
#pragma warning restore SKEXP0010
};

const string prompt = """
    Create a list of must-do experiences before dying, formatted as JSON. Ensure the list covers diverse activities across different age ranges. Follow this format:

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

    Make sure the list includes adventurous, cultural, personal growth, and unique experiences. Keep `recommendedAge` relevant but flexible. Provide at least 15 items.
    """;

var textContentStream =
    textGenerationService.GetStreamingTextContentsAsync(prompt, openAiPromptExecutionSettings);

var parser = new TodoStateMachineJsonTokenParser(PrintName, PrintItem);
var feeder = new JsonAsyncStreamTokenFeeder(parser, chunkBufferSize: 32);
await feeder.FeedAsync(textContentStream);

return;

void PrintName(string name)
{
    Console.WriteLine(name);
    Console.WriteLine("--------------");
}

void PrintItem(TodoItem item) => Console.WriteLine("{0}. {1} at {2}", item.Index, item.Description, item.RecommendedAge);