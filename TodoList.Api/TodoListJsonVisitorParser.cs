using System.Text.Json;
using StreamingJsonParser;

namespace TodoList.Api;

public enum TodoParsingState
{
    None,
    ReadingName,
    ReadingItems,
    ReadingItem,
    ReadingItemRecommendedAge,
    ReadingItemDescription,
}

public class ListState
{
    public string? Name { get; set; }

    public TodoListCreatedEvent ToEvent()
    {
        if (Name is null)
        {
            throw new JsonException("List name is missing.");
        }

        return new TodoListCreatedEvent(Name);
    }
}

public class ItemAddedState
{
    public int? RecommendedAge { get; set; }
    public string? Description { get; set; }
        
    public void Reset()
    {
        RecommendedAge = null;
        Description = null;
    }

    public TodoListItemAddedEvent ToEvent()
    {
        if (Description is null)
        {
            throw new JsonException("Item description is missing.");
        }

        if (!RecommendedAge.HasValue)
        {
            throw new JsonException("Item age is missing.");
        }    
        
        return new TodoListItemAddedEvent(RecommendedAge.Value, Description);
    }
}

public class TodoListJsonVisitorParser() : IIncrementalJsonParser<TodoListBaseEvent>
{
    private readonly ListState _listState = new();
    private readonly ItemAddedState _listItemState = new();
    private TodoParsingState _parsingState = TodoParsingState.None;

    /// <inheritdoc />
    public TodoListBaseEvent[] ContinueParsing(ref Utf8JsonReader reader, ref bool completed)
    {
        List<TodoListBaseEvent> results = [];

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    _parsingState = reader.GetString() switch
                    {
                        "listName" => TodoParsingState.ReadingName,
                        "items" => TodoParsingState.ReadingItems,
                        "recommendedAge" => TodoParsingState.ReadingItemRecommendedAge,
                        "description" => TodoParsingState.ReadingItemDescription,
                        _ => TodoParsingState.None
                    };
                    break;
                case JsonTokenType.String:
                    var stringValue = reader.GetString() ?? string.Empty;
                    if (_parsingState == TodoParsingState.ReadingName)
                    {
                        _listState.Name = stringValue;
                        results.Add(_listState.ToEvent());
                        _parsingState = TodoParsingState.None;
                    }
                    else if (_parsingState == TodoParsingState.ReadingItemDescription)
                    {
                        _listItemState.Description = stringValue;
                        _parsingState = TodoParsingState.ReadingItem;
                    }

                    break;
                case JsonTokenType.Number:
                    if (_parsingState == TodoParsingState.ReadingItemRecommendedAge &&
                        reader.TryGetInt32(out var intValue))
                    {
                        _listItemState.RecommendedAge = intValue;
                        _parsingState = TodoParsingState.ReadingItem;
                    }

                    break;
                case JsonTokenType.StartArray:
                    if (_parsingState == TodoParsingState.ReadingItems)
                    {
                        _parsingState = TodoParsingState.ReadingItem;
                    }

                    break;
                case JsonTokenType.StartObject:
                    if (_parsingState == TodoParsingState.ReadingItem)
                    {
                        _listItemState.Reset();
                    }

                    break;
                case JsonTokenType.EndObject:
                    if (_parsingState == TodoParsingState.ReadingItem)
                    {
                        results.Add(_listItemState.ToEvent());
                        _parsingState = TodoParsingState.ReadingItems;
                    }

                    break;
                case JsonTokenType.EndArray:
                    if (_parsingState == TodoParsingState.ReadingItems)
                    {
                        completed = true;
                    }

                    break;
            }
        }

        return results.ToArray();
    }
}