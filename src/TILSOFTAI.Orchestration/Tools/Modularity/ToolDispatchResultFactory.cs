namespace TILSOFTAI.Orchestration.Tools.Modularity;

public static class ToolDispatchResultFactory
{
    public static ToolDispatchResult Create(object normalizedIntent, ToolExecutionResult result, ToolDispatchExtras? extras = null)
        => new(normalizedIntent, result, extras ?? ToolDispatchExtras.Empty);
}
