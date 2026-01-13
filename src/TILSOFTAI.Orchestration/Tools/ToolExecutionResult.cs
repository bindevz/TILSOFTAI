namespace TILSOFTAI.Orchestration.Tools;

public sealed record ToolExecutionResult(bool Success, string Message, object Data)
{
    public static ToolExecutionResult CreateSuccess(string message, object data) => new(true, message, data);
    public static ToolExecutionResult CreateFailure(string message) => new(false, message, new { });

    /// <summary>
    /// Failure result that still carries a structured payload.
    /// Use this when you want the LLM to recover intelligently (e.g., parameter governance, catalog missing).
    /// </summary>
    public static ToolExecutionResult CreateFailure(string message, object data) => new(false, message, data);
}
