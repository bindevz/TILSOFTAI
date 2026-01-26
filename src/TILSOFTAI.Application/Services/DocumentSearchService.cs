using Microsoft.Extensions.Options;
using TILSOFTAI.Configuration;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Application.Services;

public sealed class DocumentSearchService
{
    private readonly IDocumentSearchRepository _repo;
    private readonly IEmbeddingClient _embeddings;
    private readonly DocumentSearchSettings _settings;

    public DocumentSearchService(
        IDocumentSearchRepository repo,
        IEmbeddingClient embeddings,
        IOptions<AppSettings> appSettings)
    {
        _repo = repo;
        _embeddings = embeddings;
        _settings = appSettings.Value.DocumentSearch;
    }

    public async Task<IReadOnlyList<DocumentChunkHit>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        query = (query ?? string.Empty).Trim();
        if (query.Length == 0) return Array.Empty<DocumentChunkHit>();

        topK = topK < 1 ? 1 : topK;
        if (_settings.MaxTopK > 0)
            topK = Math.Min(topK, _settings.MaxTopK);

        var vec = await _embeddings.CreateEmbeddingAsync(query, cancellationToken);
        return await _repo.SearchByVectorAsync(vec, topK, cancellationToken);
    }
}
