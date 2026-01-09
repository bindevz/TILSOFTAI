using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

public sealed class ModelsPriceAnalyzeToolHandler : IToolHandler
{
    public string ToolName => "models.price.analyze";

    private readonly ModelsService _modelsService;

    public ModelsPriceAnalyzeToolHandler(ModelsService modelsService)
    {
        _modelsService = modelsService;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var modelId = dyn.GetGuid("modelId");
        var analysis = await _modelsService.AnalyzePriceAsync(modelId, context, cancellationToken);

        var table = new TabularData(
            Columns: new[]
            {
                new TabularColumn("basePrice", TabularType.Decimal),
                new TabularColumn("adjustment", TabularType.Decimal),
                new TabularColumn("finalPrice", TabularType.Decimal)
            },
            Rows: new[]
            {
                new object?[] { analysis.BasePrice, analysis.Adjustment, analysis.FinalPrice }
            },
            TotalCount: 1);

        var payload = new
        {
            kind = "models.price.analyze.v2",
            schemaVersion = 2,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.price.analyze",
            data = new
            {
                modelId,
                table
            },
            warnings = Array.Empty<string>()
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.price.analyze executed", payload));
    }
}
