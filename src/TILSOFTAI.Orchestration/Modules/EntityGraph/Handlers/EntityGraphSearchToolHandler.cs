using Microsoft.Extensions.Logging;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.EntityGraph.Handlers;

public sealed class EntityGraphSearchToolHandler : IToolHandler
{
    public string ToolName => "atomic.graph.search";

    private readonly EntityGraphService _service;
    private readonly ILogger<EntityGraphSearchToolHandler> _logger;

    public EntityGraphSearchToolHandler(EntityGraphService service, ILogger<EntityGraphSearchToolHandler> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task<ToolDispatchResult> HandleAsync(
        object intent,
        TSExecutionContext context,
        CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var query = dyn.GetStringRequired("query");
        var topK = dyn.GetInt("topK", 5);

        _logger.LogInformation("EntityGraphSearch start q={Query} topK={TopK}", query, topK);
        var results = await _service.SearchAsync(query, topK, cancellationToken);
        _logger.LogInformation("EntityGraphSearch end q={Query} hits={Hits}", query, results.Count);

        var payload = new
        {
            kind = "atomic.graph.search.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "atomic.graph.search",
            data = new
            {
                query,
                topK,
                results = results.Select(r => new { r.GraphCode, r.Domain, r.Entity, r.DescriptionEn, r.Score })
            }
        };

        // Evidence (bounded + includes pack hints)
var evidenceHits = results
    .OrderByDescending(r => r.Score)
    .ThenByDescending(r => r.UpdatedAtUtc)
    .Take(topK)
    .Select(r => new
    {
        r.GraphId,
        r.GraphCode,
        r.Domain,
        r.Entity,
        r.Tags,
        r.RootSpName,
        r.Score,
        r.UpdatedAtUtc,
        Packs = (r.Packs ?? Array.Empty<EntityGraphPackHint>())
            .OrderBy(p => p.SortOrder)
            .Take(10)
            .Select(p => new { p.PackCode, p.PackType, p.SpName, p.Tags, p.SortOrder })
            .ToList()
    })
    .ToList();

var evidence = new List<EnvelopeEvidenceItemV1>
{
    new EnvelopeEvidenceItemV1
    {
        Id = "graph_search_hits",
        Type = "list",
        Title = "Graph Search Hits",
        Payload = new { query, topK, hits = evidenceHits }
    }
};

var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1 { System = "sqlserver", Name = "dbo.TILSOFTAI_EntityGraphCatalog", Cache = "na" },
            Evidence: evidence);

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("atomic.graph.search executed", payload), extras);
    }
}
