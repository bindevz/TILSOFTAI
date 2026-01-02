using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Orchestration.SK;

public sealed class ExecutionContextAccessor
{
    public TSExecutionContext Context { get; set; }
    public string? ConfirmedConfirmationId { get; set; } // parse từ user message

    // Per-request circuit breaker state for auto tool invocation
    public int AutoInvokeCount { get; set; }
    // Whether server-side tool loop guardrail was triggered for this request.
    public bool CircuitBreakerTripped { get; set; }
    public string? CircuitBreakerReason { get; set; }
    public Dictionary<string, int> AutoInvokeSignatureCounts { get; } = new(StringComparer.Ordinal);
}