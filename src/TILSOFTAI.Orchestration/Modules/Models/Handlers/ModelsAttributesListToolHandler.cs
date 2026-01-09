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
        //var modelId = dyn.GetGuid("modelId");
        ////var attrs = await _modelsService.ListAttributesAsync(modelId, context, cancellationToken);

        //var table = new TabularData(
        //    Columns: new[]
        //    {
        //        new TabularColumn("name", TabularType.String),
        //        new TabularColumn("value", TabularType.String)
        //    },
        //    Rows: attrs.Select(a => new object?[] { a.Name, a.Value }).ToArray(),
        //    TotalCount: attrs.Count);

        //var payload = new
        //{
        //    kind = "models.attributes.list.v2",
        //    schemaVersion = 2,
        //    generatedAtUtc = DateTimeOffset.UtcNow,
        //    resource = "models.attributes.list",
        //    data = new
        //    {
        //        modelId,
        //        table
        //    },
        //    warnings = Array.Empty<string>()
        //};
        var payload = new { };
        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.attributes.list executed", payload));
    }
}
