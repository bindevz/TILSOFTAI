using System.Text.Json.Serialization;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "lmstudio";

    [JsonPropertyName("messages")]
    public IReadOnlyCollection<ChatCompletionMessage> Messages { get; init; } = Array.Empty<ChatCompletionMessage>();

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }
}

public sealed class ChatCompletionMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = "user";

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

public sealed class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("object")]
    public required string Object { get; init; }

    [JsonPropertyName("created")]
    public required long Created { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("choices")]
    public required IReadOnlyCollection<ChatCompletionChoice> Choices { get; init; }

    [JsonPropertyName("usage")]
    public required ChatUsage Usage { get; init; }
}

public sealed class ChatCompletionChoice
{
    [JsonPropertyName("index")]
    public required int Index { get; init; }

    [JsonPropertyName("message")]
    public required ChatCompletionMessage Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public required string FinishReason { get; init; }
}

public sealed class ChatUsage
{
    [JsonPropertyName("prompt_tokens")]
    public required int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public required int CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public required int TotalTokens { get; init; }
}
