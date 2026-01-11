using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Analytics.Handlers;

public sealed class AtomicCatalogSearchToolHandler : IToolHandler
{
    public string ToolName => "atomic.catalog.search";

    private readonly AtomicCatalogService _catalog;

    public AtomicCatalogSearchToolHandler(AtomicCatalogService catalog)
    {
        _catalog = catalog;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;

        var query = dyn.GetStringRequired("query");
        var topK = dyn.GetInt("topK", 5);

        var hits = await _catalog.SearchAsync(query, topK, cancellationToken);

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
            warnings = Array.Empty<string>()
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("atomic.catalog.search executed", payload));
    }
}
