using System.Text.Json;

namespace AsyncEnumerableJsonParser;
    
public interface IIncrementalJsonStreamParser
{
    void ContinueParsing(ref Utf8JsonReader reader);
}