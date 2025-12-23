namespace TILSOFTAI.Orchestration.Tools;

public sealed record ToolExecutionResult(bool Success, string Message, object Data)
{
    public static ToolExecutionResult CreateSuccess(string message, object data) => new(true, message, data);
    public static ToolExecutionResult CreateFailure(string message) => new(false, message, new { });
}
