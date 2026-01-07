using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

public sealed class ModelsCreatePrepareToolHandler : IToolHandler
{
    public string ToolName => "models.create.prepare";

    private readonly ModelsService _modelsService;

    public ModelsCreatePrepareToolHandler(ModelsService modelsService)
    {
        _modelsService = modelsService;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var name = dyn.GetStringRequired("name");
        var category = dyn.GetStringRequired("category");
        var basePrice = dyn.GetDecimal("basePrice");
        var attributes = dyn.GetStringMap("attributes");

        var result = await _modelsService.PrepareCreateAsync(name, category, basePrice, attributes, context, cancellationToken);

        var payload = new
        {
            kind = "models.create.prepare.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.create.prepare",
            data = result,
            warnings = Array.Empty<string>()
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.create.prepare executed", payload));
    }
}
