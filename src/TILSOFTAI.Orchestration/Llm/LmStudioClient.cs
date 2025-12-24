using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TILSOFTAI.Orchestration.Chat;

namespace TILSOFTAI.Orchestration.Llm;

public sealed class LmStudioOptions
{
    public string BaseUrl { get; set; } = "http://192.168.8.247:6688";
    public string Model { get; set; } = "tilsoftai-orchestrator";
    public int TimeoutSeconds { get; set; } = 30;
    public Dictionary<string, string> ModelMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "tilsoftai-orchestrator", "openai/gpt-oss-20b" },
        { "tilsoftai-conversation", "openai/gpt-oss-20b" }
    };
}

public sealed class LmStudioClient
{
    private readonly HttpClient _httpClient;
    private readonly LmStudioOptions _options;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public LmStudioClient(HttpClient httpClient, LmStudioOptions options)
    {
        _httpClient = httpClient;
        _options = options;
        _httpClient.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(options.BaseUrl);
        }
    }

    public async Task<string?> GetToolIntentAsync(string? model, IEnumerable<ChatCompletionMessage> messages, string systemPrompt, CancellationToken cancellationToken)
    {
        var resolvedModel = ResolveModel(model);
        var payload = new
        {
            model = resolvedModel,
            messages = BuildMessages(systemPrompt, messages),
            temperature = 0.0,
            max_tokens = 256
        };

        using var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", payload, _serializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var completion = await JsonSerializer.DeserializeAsync<OpenAiLikeResponse>(contentStream, _serializerOptions, cancellationToken);
        return completion?.Choices.FirstOrDefault()?.Message.Content;
    }

    private static IEnumerable<object> BuildMessages(string systemPrompt, IEnumerable<ChatCompletionMessage> messages)
    {
        yield return new { role = "system", content = systemPrompt };
        foreach (var message in messages)
        {
            yield return new { role = message.Role, content = message.Content };
        }
    }

    public async Task<string?> GetConversationAsync(string? model, IEnumerable<ChatCompletionMessage> messages, CancellationToken cancellationToken)
    {
        var resolvedModel = ResolveModel(model);
        var payload = new
        {
            model = resolvedModel,
            messages = messages.Select(m => new { role = m.Role, content = m.Content }),
            temperature = 0.7,
            max_tokens = 512
        };

        using var response = await _httpClient.PostAsJsonAsync("/v1/chat/completions", payload, _serializerOptions, cancellationToken);
        response.EnsureSuccessStatusCode();

        var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var completion = await JsonSerializer.DeserializeAsync<OpenAiLikeResponse>(contentStream, _serializerOptions, cancellationToken);
        return completion?.Choices.FirstOrDefault()?.Message.Content;
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
