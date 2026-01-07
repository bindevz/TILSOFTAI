using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.ActionsCatalog;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Common.Handlers;

public sealed class ActionsCatalogToolHandler : IToolHandler
{
    public string ToolName => "actions.catalog";

    private readonly IActionsCatalogService _actionsCatalogService;

    public ActionsCatalogToolHandler(IActionsCatalogService actionsCatalogService)
    {
        _actionsCatalogService = actionsCatalogService;
    }

    public Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var action = dyn.GetString("action");
        var includeExamples = dyn.GetBool("includeExamples", false);

        var catalog = _actionsCatalogService.Catalog(action, includeExamples);
        var payload = new
        {
            kind = "actions.catalog.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "actions.catalog",
            data = catalog,
            warnings = Array.Empty<string>()
        };

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1
            {
                System = "registry",
                Name = "ActionsCatalogRegistry",
                Cache = "hit",
                Note = "In-memory actions registry"
            },
            Evidence: new[]
            {
                new EnvelopeEvidenceItemV1
                {
                    Id = "ev_actions_catalog",
                    Type = "list",
                    Title = "Danh sách actions & schema",
                    Payload = new { action = action ?? string.Empty, includeExamples, catalog }
                }
            });

        return Task.FromResult(ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("actions.catalog executed", payload), extras));
    }
}
