using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.SemanticKernel;

namespace AsyncEnumerableJsonParser;

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

                // Parse as much as possible
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