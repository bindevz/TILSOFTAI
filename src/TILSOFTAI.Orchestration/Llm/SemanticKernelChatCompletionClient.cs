using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TILSOFTAI.Orchestration.Chat;

namespace TILSOFTAI.Orchestration.Llm;

public sealed class SemanticKernelChatCompletionClient : IChatCompletionClient
{
    private readonly HttpClient _httpClient;
    private readonly LmStudioOptions _options;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public SemanticKernelChatCompletionClient(HttpClient httpClient, LmStudioOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(options.BaseUrl);
        }
    }

    public async Task<string?> GetCompletionAsync(string? model, IEnumerable<ChatCompletionMessage> messages, string systemPrompt, int maxTokens, double temperature, CancellationToken cancellationToken)
    {
        var resolvedModel = ResolveModel(model);
        var payload = new
        {
            model = resolvedModel,
            messages = BuildMessages(systemPrompt, messages),
            temperature = temperature,
            max_tokens = maxTokens
        };

        using var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", payload, _serializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var completion = await JsonSerializer.DeserializeAsync<OpenAiLikeResponse>(contentStream, _serializerOptions, cancellationToken);
        return completion?.Choices.FirstOrDefault()?.Message.Content;
    }

    private IEnumerable<object> BuildMessages(string systemPrompt, IEnumerable<ChatCompletionMessage> messages)
    {
        yield return new { role = "system", content = systemPrompt };
        foreach (var message in messages)
        {
            if (IsUserOrAssistant(message.Role) && !string.IsNullOrWhiteSpace(message.Content))
            {
                yield return new { role = message.Role, content = message.Content };
            }
        }
    }

    private string ResolveModel(string? requestedModel)
    {
        var logical = requestedModel ?? _options.Model;
        if (_options.ModelMap.TryGetValue(logical, out var mapped))
        {
            return mapped;
        }

        throw new InvalidOperationException("Model not available.");
    }

    private static bool IsUserOrAssistant(string? role) =>
        string.Equals(role, "user", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase);

    private sealed class OpenAiLikeResponse
    {
        [JsonPropertyName("choices")]
        public IReadOnlyCollection<Choice> Choices { get; init; } = Array.Empty<Choice>();
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public required Message Message { get; init; }
    }

    private sealed class Message
    {
        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }
}
