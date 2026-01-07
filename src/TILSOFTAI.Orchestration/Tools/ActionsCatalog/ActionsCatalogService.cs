namespace TILSOFTAI.Orchestration.Tools.ActionsCatalog;

public sealed class ActionsCatalogService : IActionsCatalogService
{
    private readonly IActionsCatalogRegistry _registry;

    public ActionsCatalogService(IActionsCatalogRegistry registry)
    {
        _registry = registry;
    }

    public object Catalog(string? action = null, bool includeExamples = false)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            var items = _registry.List().Select(x => ToDto(x, includeExamples)).ToList();
            return new
            {
                contract = "actions.catalog.v1",
                data = new
                {
                    actions = items
                }
            };
        }

        if (_registry.TryGet(action.Trim(), out var d))
        {
            return new
            {
                contract = "actions.catalog.v1",
                data = new
                {
                    action = ToDto(d, includeExamples)
                }
            };
        }

        return new
        {
            contract = "actions.catalog.v1",
            error = $"Unknown action '{action}'.",
            data = new { actions = _registry.List().Select(x => x.Action).OrderBy(x => x).ToList() }
        };
    }

    private static object ToDto(ActionDescriptor d, bool includeExamples)
        => new
        {
            d.Action,
            d.PrepareTool,
            d.CommitTool,
            d.Description,
            parameters = d.Parameters,
            examples = includeExamples
                ? new { prepare = d.ExamplePrepareArgs, commit = d.ExampleCommitArgs }
                : null
        };
}
