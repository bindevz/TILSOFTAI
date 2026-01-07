using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Filters;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

public sealed class ModelsSearchToolHandler : IToolHandler
{
    public string ToolName => "models.search";

    private readonly ModelsService _modelsService;
    private readonly IFilterCanonicalizer _canonicalizer;

    public ModelsSearchToolHandler(ModelsService modelsService, IFilterCanonicalizer canonicalizer)
    {
        _modelsService = modelsService;
        _canonicalizer = canonicalizer;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var (filtersApplied, rejected) = _canonicalizer.Canonicalize("models.search", dyn.Filters);
        if (filtersApplied.TryGetValue("season", out var seasonRaw))
        {
            filtersApplied["season"] = SeasonNormalizer.NormalizeToFull(seasonRaw);
        }

        var result = await _modelsService.SearchAsync(
            context.TenantId,
            filtersApplied.GetValueOrDefault("rangeName"),
            filtersApplied.GetValueOrDefault("modelCode"),
            filtersApplied.GetValueOrDefault("modelName"),
            filtersApplied.GetValueOrDefault("season"),
            filtersApplied.GetValueOrDefault("collection"),
            dyn.Page,
            dyn.PageSize,
            context,
            cancellationToken);

        var payload = new
        {
            kind = "models.search.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.search",
            filtersApplied,
            rejectedFilters = rejected,
            data = new
            {
                totalCount = result.TotalCount,
                pageNumber = result.PageNumber,
                pageSize = result.PageSize,
                items = result.Items.Select(m => new { m.ModelID, m.ModelUD, m.ModelNM, m.Season, m.Collection, m.RangeName })
            },
            warnings = Array.Empty<string>()
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.search executed", payload));
    }
}
