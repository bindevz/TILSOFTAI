using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Application.Services;

public sealed class EntityGraphService
{
    private readonly IEntityGraphRepository _repository;
    private readonly ILogger<EntityGraphService> _logger;

    public EntityGraphService(
        IEntityGraphRepository repository,
        ILogger<EntityGraphService>? logger = null)
    {
        _repository = repository;
        _logger = logger ?? NullLogger<EntityGraphService>.Instance;
    }

    public async Task<IReadOnlyList<EntityGraphSearchHit>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        query = (query ?? string.Empty).Trim();
        topK = Math.Clamp(topK, 1, 20);

        // Fail-closed on empty query? User spec said "Trim query; fail-closed on empty"
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<EntityGraphSearchHit>();
        }

        return await _repository.SearchAsync(query, topK, cancellationToken);
    }

    public async Task<EntityGraphDefinition?> GetByCodeAsync(
        string graphCode,
        CancellationToken cancellationToken)
    {
        graphCode = (graphCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(graphCode))
        {
            return null;
        }

        var graph = await _repository.GetByCodeAsync(graphCode, cancellationToken);
        if (graph == null) return null;

        // "Digest bounds: maxPacks=20, maxNodes=40, maxEdges=60, maxGlossary=100"
        return new EntityGraphDefinition(
            graph.Summary,
            graph.Packs.OrderBy(p => p.SortOrder).Take(20).ToList(),
            graph.Nodes.OrderBy(n => n.DatasetName).Take(40).ToList(),
            graph.Edges.OrderBy(e => e.LeftDataset).Take(60).ToList(),
            graph.Glossary.OrderBy(g => g.Term).Take(100).ToList()
        );
    }
}
