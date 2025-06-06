using System.Text.Json.Serialization;

namespace TodoList.Api;

[Serializable]
[JsonDerivedType(typeof(TodoListCreatedEvent), typeDiscriminator: "todoListCreated")]
[JsonDerivedType(typeof(TodoListItemAddedEvent), typeDiscriminator: "todoListItemAdded")]
public abstract record TodoListBaseEvent();

[Serializable]
public record TodoListCreatedEvent(string Name) : TodoListBaseEvent;

[Serializable]
public record TodoListItemAddedEvent(int RecommendedAge, string Description) : TodoListBaseEvent;


[JsonSerializable(typeof(TodoListBaseEvent))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}