using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TILSOFTAI.Orchestration.Llm;

namespace TILSOFTAI.Orchestration.Llm.OpenAi;

/// <summary>
/// Thin HTTP client for OpenAI-compatible Chat Completions endpoint (LM Studio).
/// </summary>
public sealed class OpenAiChatClient
{
    private readonly HttpClient _http;
    private readonly LmStudioOptions _lm;
    private readonly ILogger<OpenAiChatClient> _logger;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public OpenAiChatClient(HttpClient http, LmStudioOptions lm, ILogger<OpenAiChatClient>? logger = null)
    {
        _http = http;
        _lm = lm;
        _logger = logger ?? NullLogger<OpenAiChatClient>.Instance;

        // Ensure base address is set even if DI forgets.
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_lm.BaseUrl.TrimEnd('/') + "/v1/");

        if (_http.Timeout == TimeSpan.Zero)
            _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(_lm.TimeoutSeconds, 5, 300));

        // LM Studio accepts any bearer token.
        if (_http.DefaultRequestHeaders.Authorization is null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "lm-studio");
    }

    public string MapModel(string? requestedModel)
    {
        var logical = string.IsNullOrWhiteSpace(requestedModel) ? _lm.Model : requestedModel;
        if (_lm.ModelMap.TryGetValue(logical, out var mapped))
            return mapped;

        return _lm.ModelMap.TryGetValue(_lm.Model, out var fallback) ? fallback : logical;
    }

    public async Task<OpenAiChatCompletionResponse> CreateChatCompletionAsync(OpenAiChatCompletionRequest request, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(request, _json);
        using var body = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("OpenAiChatClient POST chat/completions model={Model} msgs={MsgCount} tools={ToolCount}",
            request.Model,
            request.Messages?.Count ?? 0,
            request.Tools?.Count ?? 0);

        using var resp = await _http.PostAsync("chat/completions", body, ct);
        var respText = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAiChatClient failed status={Status} body={Body}", (int)resp.StatusCode, Truncate(respText, 1200));
            throw new HttpRequestException($"OpenAI-compatible endpoint returned {(int)resp.StatusCode}: {respText}");
        }

        var parsed = JsonSerializer.Deserialize<OpenAiChatCompletionResponse>(respText, _json);
        if (parsed is null)
            throw new InvalidOperationException("Failed to parse OpenAI chat completion response.");

        return parsed;
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "...";
    }
}
