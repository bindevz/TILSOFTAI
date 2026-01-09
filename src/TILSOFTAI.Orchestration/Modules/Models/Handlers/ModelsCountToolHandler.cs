using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Filters;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

public sealed class ModelsCountToolHandler : IToolHandler
{
    public string ToolName => "models.count";

    private readonly ModelsService _modelsService;
    private readonly IFilterCanonicalizer _canonicalizer;

    public ModelsCountToolHandler(ModelsService modelsService, IFilterCanonicalizer canonicalizer)
    {
        _modelsService = modelsService;
        _canonicalizer = canonicalizer;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var (filtersApplied, rejected) = _canonicalizer.Canonicalize("models.count", dyn.Filters);
        if (filtersApplied.TryGetValue("season", out var seasonRaw))
        {
            filtersApplied["season"] = SeasonNormalizer.NormalizeToFull(seasonRaw);
        }

        // Reuse the search stored procedure: request 1 row, read TotalCount.
        var table = await _modelsService.SearchTabularAsync(
            context.TenantId,
            filtersApplied.GetValueOrDefault("rangeName"),
            filtersApplied.GetValueOrDefault("modelCode"),
            filtersApplied.GetValueOrDefault("modelName"),
            filtersApplied.GetValueOrDefault("season"),
            filtersApplied.GetValueOrDefault("collection"),
            page: 1,
            size: 1,
            context: context,
            cancellationToken: cancellationToken);

        var total = table.TotalCount ?? 0;

        var payload = new
        {
            kind = "models.count.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.count",
            filtersApplied,
            rejectedFilters = rejected,
            data = new { totalCount = total },
            warnings = Array.Empty<string>()
        };

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1
            {
                System = "sqlserver",
                Name = "dbo.TILSOFTAI_sp_models_search",
                Cache = "na",
                Note = "Count uses search SP (pageSize=1)"
            },
            Evidence: new[]
            {
                new EnvelopeEvidenceItemV1
                {
                    Id = "ev_totalCount",
                    Type = "metric",
                    Title = "Tổng số model (theo bộ lọc)",
                    Payload = new { totalCount = total, filtersApplied }
                }
            });

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.count executed", payload), extras);
    }
}
