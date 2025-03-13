using AsyncEnumerableJsonParser;

namespace TodoListGenerator;

public enum TodoStateMachineState
{
    None,
    ReadingListName,
    ReadingItems,
    ReadingItem,
    ReadingItemRecommendedAge,
    ReadingItemDescription
}

public record TodoItem(int Index, int RecommendedAge, string Description);

public class TodoStateMachineJsonTokenParser(
    Action<string> onListNameParsed,
    Action<TodoItem> onItemParsed) : StateMachineJsonTokenParser<TodoStateMachineState>(TodoStateMachineState.None)
{
    private int _index = 1;
    private int? _currentItemRecommendedAge;
    private string? _currentItemDescription;

    protected override TodoStateMachineState VisitProperty(TodoStateMachineState stateMachineState, string propertyName)
    {
        return propertyName switch
        {
            "listName" => TodoStateMachineState.ReadingListName,
            "items" => TodoStateMachineState.ReadingItems,
            "recommendedAge" => TodoStateMachineState.ReadingItemRecommendedAge,
            "description" => TodoStateMachineState.ReadingItemDescription,
            _ => TodoStateMachineState.None
        };
    }

    protected override TodoStateMachineState VisitStringValue(TodoStateMachineState stateMachineState, string value)
    {
        switch (stateMachineState)
        {
            case TodoStateMachineState.ReadingListName:
                onListNameParsed(value);
                return TodoStateMachineState.None;
            case TodoStateMachineState.ReadingItemDescription:
                _currentItemDescription = value;
                return TodoStateMachineState.ReadingItem;
            default:
                return TodoStateMachineState.None;
        }
    }

    protected override TodoStateMachineState VisitIntValue(TodoStateMachineState stateMachineState, int value)
    {
        switch (stateMachineState)
        {
            case TodoStateMachineState.ReadingItemRecommendedAge:
                _currentItemRecommendedAge = value;
                return TodoStateMachineState.ReadingItem;
            default:
                return TodoStateMachineState.None;
        }
    }

    protected override TodoStateMachineState VisitStartArray(TodoStateMachineState stateMachineState)
    {
        return stateMachineState switch
        {
            TodoStateMachineState.ReadingItems => TodoStateMachineState.ReadingItem,
            _ => stateMachineState
        };
    }

    protected override TodoStateMachineState VisitEndObject(TodoStateMachineState stateMachineState)
    {
        switch (stateMachineState)
        {
            case TodoStateMachineState.ReadingItem:
                onItemParsed(new TodoItem(_index, _currentItemRecommendedAge!.Value, _currentItemDescription!));
                _index++;
                return TodoStateMachineState.ReadingItems;
        }

        return stateMachineState;
    }
}