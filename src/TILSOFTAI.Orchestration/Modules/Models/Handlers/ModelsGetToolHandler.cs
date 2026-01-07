using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

public sealed class ModelsGetToolHandler : IToolHandler
{
    public string ToolName => "models.get";

    private readonly ModelsService _modelsService;

    public ModelsGetToolHandler(ModelsService modelsService)
    {
        _modelsService = modelsService;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var modelId = dyn.GetGuid("modelId");
        var model = await _modelsService.GetAsync(modelId, context, cancellationToken);

        // Keep payload compatible with ver12 contract.
        var payload = new
        {
            kind = "models.get.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.get",
            data = new
            {
                model?.ModelID,
                model?.ModelUD,
                model?.ModelNM,
                model?.Season,
                model?.Collection,
                model?.RangeName
            },
            warnings = Array.Empty<string>()
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.get executed", payload));
    }
}
