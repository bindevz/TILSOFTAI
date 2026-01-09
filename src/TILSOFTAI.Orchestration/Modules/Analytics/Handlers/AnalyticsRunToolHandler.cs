using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Analytics.Handlers;

public sealed class AnalyticsRunToolHandler : IToolHandler
{
    public string ToolName => "analytics.run";

    private readonly AnalyticsService _analyticsService;

    public AnalyticsRunToolHandler(AnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var datasetId = dyn.GetStringRequired("datasetId");
        var pipeline = dyn.GetJsonRequired("pipeline");

        var bounds = new AnalyticsService.RunBounds(
            TopN: dyn.GetInt("topN", 20),
            MaxGroups: dyn.GetInt("maxGroups", 200),
            MaxResultRows: dyn.GetInt("maxResultRows", 500));

        var result = await _analyticsService.RunAsync(datasetId, pipeline, bounds, context, cancellationToken);

        var payload = new
        {
            kind = "analytics.run.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "analytics.run",
            data = new
            {
                result.DatasetId,
                result.RowCount,
                result.ColumnCount,
                schema = result.Schema,
                rows = new
                {
                    columns = result.Schema.Select(c => c.Name),
                    values = result.Rows
                },
                warnings = result.Warnings
            }
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("analytics.run executed", payload));
    }
}
