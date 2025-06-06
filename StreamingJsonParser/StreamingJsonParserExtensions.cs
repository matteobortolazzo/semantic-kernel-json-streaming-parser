using System.Buffers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.SemanticKernel;

namespace StreamingJsonParser;

public static class StreamingJsonParserExtensions
{
    private static readonly byte[] NewLineBytes = Encoding.UTF8.GetBytes(Environment.NewLine);
    private const string NdJsonContentType = "application/x-ndjson";

    /// <summary>
    /// Stream an NDJSON response from an LLM JSON response, based on the used parser.
    /// </summary>
    /// <param name="input">The source.</param>
    /// <param name="incrementalParser">The parser to use.</param>
    /// <param name="httpContext">The context where to write the response.</param>
    /// <param name="chunkBufferSize">Quantity of chucks to buffer before continue parsing.</param>
    /// <param name="jsonSerializerOptions">JSON serialization options.</param>
    /// <param name="cancellationToken"></param>
    public static async Task<EmptyHttpResult> ToNdJsonAsync<TOut>(
        this IAsyncEnumerable<StreamingTextContent> input,
        HttpContext httpContext,
        IIncrementalJsonParser<TOut> incrementalParser,
        int chunkBufferSize = 48,
        JsonSerializerOptions? jsonSerializerOptions = null,
        CancellationToken cancellationToken = default)
        where TOut : class
    {
        // Control the pace of the stream by reading in chunks
        var enumerator = input.GetAsyncEnumerator(cancellationToken);

        try
        { 
            httpContext.Response.ContentType = NdJsonContentType; 
            
            // Buffer for the chunks of text
            var buffer = new ArrayBufferWriter<byte>();
            // Keep track of the state of the JSON reader
            JsonReaderState jsonReaderState = new();
            
            var completed = false;
            while (!completed)
            {
                // Load the buffer with the next chunk of text
                for (var i = 0; i < chunkBufferSize; i++)
                {
                    var readSuccess = await enumerator.MoveNextAsync();
                    // Reached the end of the stream
                    if (!readSuccess)
                    {
                        completed = true;
                        break;
                    }

                    if (enumerator.Current?.Text == null)
                    {
                        continue;
                    }
                    
                    var bytes = Encoding.UTF8.GetBytes(enumerator.Current.Text);
                    buffer.Write(bytes);
                }

                // Load the reader with the buffer
                var reader = new Utf8JsonReader(
                    buffer.WrittenSpan,
                    isFinalBlock: false, // The input might be a partial JSON
                    state: jsonReaderState);

                // Parse as much as possible
                var parsedItems = incrementalParser.ContinueParsing(ref reader, ref completed);

                // Save the parsing state
                jsonReaderState = reader.CurrentState;

                var remainingBytes = buffer.WrittenSpan[(int)reader.BytesConsumed..];

                // Reset the buffer and write the remaining bytes
                buffer.ResetWrittenCount();
                buffer.Clear();
                buffer.Write(remainingBytes);

                // Serialize each parsed item to JSON and write it to the response
                foreach (var parsedItem in parsedItems)
                {
                    var documentJson = JsonSerializer.SerializeToUtf8Bytes(parsedItem, jsonSerializerOptions);
                    await httpContext.Response.Body.WriteAsync(documentJson, cancellationToken);
                    await httpContext.Response.Body.WriteAsync(NewLineBytes, cancellationToken);
                }
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
            await httpContext.Response.Body.FlushAsync(cancellationToken);
        }

        return TypedResults.Empty;
    }
}