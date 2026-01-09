using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Filters;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Analytics.Handlers;

public sealed class AnalyticsDatasetCreateToolHandler : IToolHandler
{
    public string ToolName => "analytics.dataset.create";

    private readonly AnalyticsService _analyticsService;
    private readonly IFilterCanonicalizer _canonicalizer;

    public AnalyticsDatasetCreateToolHandler(AnalyticsService analyticsService, IFilterCanonicalizer canonicalizer)
    {
        _analyticsService = analyticsService;
        _canonicalizer = canonicalizer;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var (filtersApplied, rejected) = _canonicalizer.Canonicalize("analytics.dataset.create", dyn.Filters);

        // Reuse season normalization rules.
        if (filtersApplied.TryGetValue("season", out var seasonRaw))
            filtersApplied["season"] = SeasonNormalizer.NormalizeToFull(seasonRaw);

        var source = dyn.GetString("source") ?? "models";
        var select = dyn.GetJson("select");
        var bounds = new AnalyticsService.DatasetBounds(
            MaxRows: dyn.GetInt("maxRows", 20000),
            MaxColumns: dyn.GetInt("maxColumns", 40),
            PreviewRows: dyn.GetInt("previewRows", 100));

        var result = await _analyticsService.CreateDatasetAsync(
            source,
            filtersApplied,
            select,
            bounds,
            context,
            cancellationToken);

        var payload = new
        {
            kind = "analytics.dataset.create.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "analytics.dataset.create",
            filtersApplied,
            rejectedFilters = rejected,
            data = new
            {
                result.DatasetId,
                result.Source,
                result.RowCount,
                result.ColumnCount,
                expiresAtUtc = result.ExpiresAtUtc,
                schema = result.Schema,
                preview = new
                {
                    columns = result.Schema.Select(c => c.Name),
                    rows = result.Preview
                }
            },
            warnings = Array.Empty<string>()
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("analytics.dataset.create executed", payload));
    }
}
