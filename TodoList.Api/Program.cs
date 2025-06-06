using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.TextGeneration;
using StreamingJsonParser;
using TodoList.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOpenApi();

OpenAIChatCompletionService chatCompletionService = new(
    apiKey: Environment.GetEnvironmentVariable("OPENAI_API_KEY")!, // Replace with your OpenAI API key
    modelId: "gpt-4.1"
);
builder.Services.AddSingleton<ITextGenerationService>(chatCompletionService);
builder.Services.AddTransient<IIncrementalJsonParser<TodoListBaseEvent>, TodoListJsonVisitorParser>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

OpenAIPromptExecutionSettings openAiPromptExecutionSettings = new()
{
    ResponseFormat = "json_object"
};

JsonSerializerOptions jsonSerializer = new()
{
    WriteIndented = false, // Needs to be false for NDJSON
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    TypeInfoResolver = SourceGenerationContext.Default // Source-generated context for polymorphic serialization
};

app.MapGet("/", (
            HttpContext httpContext,
            ITextGenerationService textGenerationService,
            IIncrementalJsonParser<TodoListBaseEvent> parser,
            CancellationToken cancellationToken) =>
        textGenerationService
            .GetStreamingTextContentsAsync(Prompts.GenerateTodoListPrompt,
                executionSettings: openAiPromptExecutionSettings,
                cancellationToken: cancellationToken)
            .ToNdJsonAsync(
                httpContext,
                parser,
                chunkBufferSize: 48,
                jsonSerializer,
                cancellationToken))
    .WithName("StreamTodoListEvents");

app.Run();

internal static class Prompts
{
    public const string GenerateTodoListPrompt =
        """
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
}