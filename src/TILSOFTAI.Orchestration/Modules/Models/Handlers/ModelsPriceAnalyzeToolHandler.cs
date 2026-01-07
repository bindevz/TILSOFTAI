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

        var payload = new
        {
            kind = "models.price.analyze.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.price.analyze",
            data = analysis,
            warnings = Array.Empty<string>()
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.price.analyze executed", payload));
    }
}
