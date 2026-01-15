namespace TILSOFTAI.Orchestration.SK;

public sealed class ExecutionContextAccessor
{
    public TILSOFTAI.Domain.ValueObjects.TSExecutionContext Context { get; set; }
    public string? ConfirmedConfirmationId { get; set; } // parse từ user message

    // Last-known answer hints gathered during tool execution within this request.
    // This allows ChatPipeline to return a deterministic fallback response when the
    // LLM enters a tool-calling loop and the circuit breaker trips.
    public int? LastTotalCount { get; set; }
    public string? LastStoredProcedure { get; set; }
    public IReadOnlyDictionary<string, object?>? LastFilters { get; set; }
    public string? LastSeasonFilter { get; set; }
    public string? LastCollectionFilter { get; set; }
    public string? LastRangeNameFilter { get; set; }
    public string? LastDisplayPreviewJson { get; set; } // compact preview JSON for circuit-breaker fallback

    // Per-request circuit breaker state for auto tool invocation
    public int AutoInvokeCount { get; set; }
    // Whether server-side tool loop guardrail was triggered for this request.
    public bool CircuitBreakerTripped { get; set; }
    public string? CircuitBreakerReason { get; set; }
    public Dictionary<string, int> AutoInvokeSignatureCounts { get; } = new(StringComparer.Ordinal);
}