using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Filters;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

/// <summary>
/// Returns models as a bounded, schema-carrying table (TabularData).
/// This removes all model-specific DTO projections from the tool payload and keeps the result usable
/// for downstream in-memory analytics (Atomic Data Engine).
/// </summary>
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

        var table = await _modelsService.SearchTabularAsync(
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
            kind = "models.search.v2",
            schemaVersion = 2,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.search",
            filtersApplied,
            rejectedFilters = rejected,
            data = new
            {
                totalCount = table.TotalCount,
                pageNumber = dyn.Page,
                pageSize = dyn.PageSize,
                table
            },
            warnings = Array.Empty<string>()
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.search executed", payload));
    }
}
