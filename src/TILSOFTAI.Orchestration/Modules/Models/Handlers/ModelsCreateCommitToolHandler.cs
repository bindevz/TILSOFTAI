using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

public sealed class ModelsCreateCommitToolHandler : IToolHandler
{
    public string ToolName => "models.create.commit";

    private readonly ModelsService _modelsService;

    public ModelsCreateCommitToolHandler(ModelsService modelsService)
    {
        _modelsService = modelsService;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        //var confirmationId = dyn.GetStringRequired("confirmationId");
        //var created = await _modelsService.CommitCreateAsync(confirmationId, context, cancellationToken);

        //var payload = new
        //{
        //    kind = "models.create.commit.v1",
        //    schemaVersion = 1,
        //    generatedAtUtc = DateTimeOffset.UtcNow,
        //    resource = "models.create.commit",
        //    data = new
        //    {
        //        created.ModelID,
        //        created.ModelUD,
        //        created.ModelNM
        //    },
        //    warnings = Array.Empty<string>()
        //};
        var payload = new { };
        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.create.commit executed", payload));
    }
}
