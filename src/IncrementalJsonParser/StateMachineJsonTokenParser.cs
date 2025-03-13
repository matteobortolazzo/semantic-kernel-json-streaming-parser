using System.Text.Json;

namespace AsyncEnumerableJsonParser;

public abstract class StateMachineJsonTokenParser<T>(T initialState) : IIncrementalJsonStreamParser
{
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
                case JsonTokenType.Number:
                    if (reader.TryGetInt32(out var intValue))
                    {
                        State = VisitIntValue(State, intValue);
                    }

                    // TODO: Handle other number types
                    break;
                case JsonTokenType.StartArray:
                    State = VisitStartArray(State);
                    break;
                case JsonTokenType.EndArray:
                    State = VisitEndArray(State);
                    break;
                case JsonTokenType.StartObject:
                    State = VisitStartObject(State);
                    break;
                case JsonTokenType.EndObject:
                    State = VisitEndObject(State);
                    break;
                // TODO: Handle True, False, Null, etc.
            }
        }
    }

    protected virtual T VisitProperty(T state, string propertyName) => state;

    protected virtual T VisitStringValue(T state, string value) => state;

    protected virtual T VisitIntValue(T state, int value) => state;

    protected virtual T VisitStartArray(T state) => state;

    protected virtual T VisitEndArray(T state) => state;

    protected virtual T VisitStartObject(T state) => state;

    protected virtual T VisitEndObject(T state) => state;
}