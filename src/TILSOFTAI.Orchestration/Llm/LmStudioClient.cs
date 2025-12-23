using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using TILSOFTAI.Orchestration.Chat;

namespace TILSOFTAI.Orchestration.Llm;

public sealed class LmStudioOptions
{
    public string BaseUrl { get; set; } = "http://localhost:1234";
    public string Model { get; set; } = "local-model";
    public int TimeoutSeconds { get; set; } = 30;
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
        var payload = new
        {
            model = model ?? _options.Model,
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
