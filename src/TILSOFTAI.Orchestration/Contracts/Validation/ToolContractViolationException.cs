namespace TILSOFTAI.Orchestration.Contracts.Validation;

/// <summary>
/// Indicates a server-side contract drift/violation between a tool response payload and
/// its governance schema. This is a non-retryable error: re-calling the LLM will not fix it.
/// </summary>
public sealed class ToolContractViolationException : Exception
{
    public ToolContractViolationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
