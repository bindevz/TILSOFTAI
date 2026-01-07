using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.FiltersCatalog;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Common.Handlers;

public sealed class FiltersCatalogToolHandler : IToolHandler
{
    public string ToolName => "filters.catalog";

    private readonly IFilterCatalogService _filterCatalogService;

    public FiltersCatalogToolHandler(IFilterCatalogService filterCatalogService)
    {
        _filterCatalogService = filterCatalogService;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var resource = dyn.GetString("resource");
        var includeValues = dyn.GetBool("includeValues", false);

        var catalog = await _filterCatalogService.GetCatalogAsync(context, resource, includeValues, cancellationToken);

        var payload = new
        {
            kind = "filters.catalog.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "filters.catalog",
            data = catalog,
            warnings = Array.Empty<string>()
        };

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1
            {
                System = "registry",
                Name = "FilterCatalogRegistry",
                Cache = "hit",
                Note = "In-memory filter registry"
            },
            Evidence: new[]
            {
                new EnvelopeEvidenceItemV1
                {
                    Id = "ev_filters_catalog",
                    Type = "list",
                    Title = "Danh sách filters hợp lệ",
                    Payload = new { resource = resource ?? string.Empty, includeValues, catalog }
                }
            });

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("filters.catalog executed", payload), extras);
    }
}
