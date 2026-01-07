namespace TILSOFTAI.Orchestration.Tools.Modularity;

/// <summary>
/// Output of a tool handler before it is wrapped into the enterprise envelope.
/// </summary>
public sealed record ToolDispatchResult(
    object NormalizedIntent,
    ToolExecutionResult Result,
    ToolDispatchExtras Extras);
