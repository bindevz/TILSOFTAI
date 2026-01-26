using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TILSOFTAI.Configuration;
using TILSOFTAI.Domain.Interfaces;

namespace TILSOFTAI.Orchestration.Llm.OpenAi;

/// <summary>
/// Thin HTTP client for OpenAI-compatible Embeddings endpoint.
/// </summary>
public sealed class OpenAiEmbeddingsClient : IEmbeddingClient
{
    private readonly HttpClient _http;
    private readonly EmbeddingsSettings _emb;
    private readonly ILogger<OpenAiEmbeddingsClient> _logger;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public OpenAiEmbeddingsClient(HttpClient http, IOptions<AppSettings> settings, ILogger<OpenAiEmbeddingsClient>? logger = null)
    {
        _http = http;
        _emb = settings.Value.Embeddings;
        _logger = logger ?? NullLogger<OpenAiEmbeddingsClient>.Instance;

        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(_emb.Endpoint.TrimEnd('/') + "/v1/");

        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(_emb.TimeoutSeconds, 5, 3600));

        if (_http.DefaultRequestHeaders.Authorization is null)
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "lm-studio");
    }

    public async Task<float[]> CreateEmbeddingAsync(string input, CancellationToken cancellationToken)
    {
        if (!_emb.Enabled)
            throw new InvalidOperationException("Embeddings are disabled in configuration.");

        input = (input ?? string.Empty).Trim();
        if (input.Length == 0)
            return Array.Empty<float>();

        var req = new OpenAiEmbeddingRequest
        {
            Model = string.IsNullOrWhiteSpace(_emb.Model) ? "text-embedding-3-small" : _emb.Model,
            Input = input,
            Dimensions = _emb.Dimensions
        };

        var json = JsonSerializer.Serialize(req, _json);
        using var body = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogInformation("OpenAiEmbeddingsClient POST embeddings model={Model} dims={Dims}", req.Model, req.Dimensions);

        using var resp = await _http.PostAsync("embeddings", body, cancellationToken);
        var respText = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("OpenAiEmbeddingsClient failed status={Status} body={Body}", (int)resp.StatusCode, Truncate(respText, 1200));
            throw new HttpRequestException($"Embeddings endpoint returned {(int)resp.StatusCode}: {respText}");
        }

        var parsed = JsonSerializer.Deserialize<OpenAiEmbeddingResponse>(respText, _json);
        var vec = parsed?.Data?.FirstOrDefault()?.Embedding;

        if (vec is null || vec.Length == 0)
            throw new InvalidOperationException("Failed to parse embeddings response or embedding is empty.");

        // Optional: strict length check
        if (_emb.Dimensions > 0 && vec.Length != _emb.Dimensions)
            _logger.LogWarning("Embedding dimension mismatch expected={Expected} got={Got}", _emb.Dimensions, vec.Length);

        return vec;
    }

    private static string Truncate(string? s, int max)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Length <= max) return s;
        return s.Substring(0, max) + "...";
    }
}
