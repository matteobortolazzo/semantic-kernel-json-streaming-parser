using System.Text.Json;

namespace StreamingJsonParser;

/// <summary>
/// An interface for incrementally parsing JSON data.
/// </summary>
/// <typeparam name="TOut"></typeparam>
public interface IIncrementalJsonParser<out TOut> where TOut : class  
{
    /// <summary>
    /// Continue parsing the JSON stream from the current position of the reader.
    /// </summary>
    /// <param name="reader"></param>
    /// <param name="completed"></param>
    /// <returns>The list of parsed objects.</returns>
    public TOut[] ContinueParsing(ref Utf8JsonReader reader, ref bool completed);
}