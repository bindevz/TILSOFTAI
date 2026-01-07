using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

public sealed class ModelsAttributesListToolHandler : IToolHandler
{
    public string ToolName => "models.attributes.list";

    private readonly ModelsService _modelsService;

    public ModelsAttributesListToolHandler(ModelsService modelsService)
    {
        _modelsService = modelsService;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var modelId = dyn.GetGuid("modelId");
        var attrs = await _modelsService.ListAttributesAsync(modelId, context, cancellationToken);

        var payload = new
        {
            kind = "models.attributes.list.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.attributes.list",
            data = new
            {
                modelId,
                attributes = attrs.Select(a => new { a.Name, a.Value })
            },
            warnings = Array.Empty<string>()
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.attributes.list executed", payload));
    }
}
