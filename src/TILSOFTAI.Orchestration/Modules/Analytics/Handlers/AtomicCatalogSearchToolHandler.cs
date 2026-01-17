using Microsoft.Extensions.Logging;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Analytics.Handlers;

public sealed class AtomicCatalogSearchToolHandler : IToolHandler
{
    public string ToolName => "atomic.catalog.search";

    private readonly AtomicCatalogService _catalog;
    private readonly ILogger<AtomicCatalogSearchToolHandler> _logger;

    public AtomicCatalogSearchToolHandler(AtomicCatalogService catalog, ILogger<AtomicCatalogSearchToolHandler> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;

        var query = dyn.GetStringRequired("query");
        var topK = dyn.GetInt("topK", 5);

        _logger.LogInformation("AtomicCatalogSearch start q={Query} topK={TopK}", query, topK);
        var hits = await _catalog.SearchAsync(query, topK, cancellationToken);
        _logger.LogInformation("AtomicCatalogSearch end q={Query} hits={Hits} top1={Top1}", query, hits.Count, hits.FirstOrDefault()?.SpName);

        var items = hits.Select(h => new
        {
            spName = h.SpName,
            domain = h.Domain,
            entity = h.Entity,
            tags = h.Tags,
            score = h.Score,
            intent = new { vi = h.IntentVi, en = h.IntentEn },
            parameters = AtomicCatalogService.ParseParamSpecs(h.ParamsJson).Select(p => new
            {
                name = p.Name,
                sqlType = p.SqlType,
                required = p.Required,
                description_vi = p.DescriptionVi,
                description_en = p.DescriptionEn,
                @default = p.DefaultValue,
                example = p.Example
            })
        });

        var warnings = new List<string>();

        if (!items.Any())
            warnings.Add("No catalog hit. Verify dbo.TILSOFTAI_SPCatalog has data, or add the required stored procedure.");

        // Contract guidance: enforce ParamsJson as the source of truth for tool inputs.
        warnings.Add("Contract: When calling atomic.query.execute, ONLY use parameter names declared in results[].parameters[].name (ParamsJson). Do NOT use output column names from RS1/RS2 (e.g., seasonFilter).");

        var payload = new
        {
            kind = "atomic.catalog.search.v1",
            schemaVersion = 2,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "atomic.catalog.search",
            data = new
            {
                query,
                topK,
                results = items
            },
            warnings = warnings.ToArray()
        };

        var evidence = new List<EnvelopeEvidenceItemV1>();

        // Evidence: always return a compact list of hits so the client/LLM can respond without looping.
        // Keep it small to avoid token bloat.
        var compactHits = hits.Take(Math.Clamp(topK, 1, 10)).Select(h => new
        {
            spName = h.SpName,
            // Avoid overload ambiguity: Score is int and can convert to both double/decimal.
            score = Math.Round((double)h.Score, 4),
            domain = h.Domain,
            entity = h.Entity
        });

        evidence.Add(new EnvelopeEvidenceItemV1
        {
            Id = "catalog_hits",
            Type = "list",
            Title = "Catalog hits",
            Payload = new { query, topK, results = compactHits }
        });

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1 { System = "sqlserver", Name = "dbo.TILSOFTAI_SPCatalog", Cache = "na" },
            Evidence: evidence);

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("atomic.catalog.search executed", payload), extras);
    }
}
