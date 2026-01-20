using System.Text.Json;
using System.Text.Json.Serialization;

namespace TILSOFTAI.Orchestration.Llm.OpenAi;

/// <summary>
/// Minimal OpenAI-compatible tool calling models (LM Studio compatible).
/// </summary>
public sealed class OpenAiChatCompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required List<OpenAiChatMessage> Messages { get; init; } = new();

    [JsonPropertyName("tools")]
    public List<OpenAiToolDefinition>? Tools { get; init; }

    /// <summary>
    /// Use "auto" (string) or an object like {"type":"function","function":{"name":"..."}}.
    /// </summary>
    [JsonPropertyName("tool_choice")]
    public object? ToolChoice { get; init; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("stream")]
    public bool? Stream { get; init; }
}

public sealed class OpenAiChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    // Assistant tool calls
    [JsonPropertyName("tool_calls")]
    public List<OpenAiToolCall>? ToolCalls { get; init; }

    // Tool response -> tool_call_id
    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }
}

public sealed class OpenAiToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public required OpenAiFunctionDefinition Function { get; init; }
}

public sealed class OpenAiFunctionDefinition
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// JSON schema for arguments.
    /// </summary>
    [JsonPropertyName("parameters")]
    public JsonElement Parameters { get; init; }
}

public sealed class OpenAiToolCall
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public required OpenAiToolCallFunction Function { get; init; }
}

public sealed class OpenAiToolCallFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// JSON string.
    /// </summary>
    [JsonPropertyName("arguments")]
    public required string Arguments { get; init; }
}

public sealed class OpenAiChatCompletionResponse
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
    public required List<OpenAiChatChoice> Choices { get; init; }

    [JsonPropertyName("usage")]
    public OpenAiChatUsage? Usage { get; init; }
}

public sealed class OpenAiChatChoice
{
    [JsonPropertyName("index")]
    public required int Index { get; init; }

    [JsonPropertyName("message")]
    public required OpenAiChatMessage Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

public sealed class OpenAiChatUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; init; }
}
