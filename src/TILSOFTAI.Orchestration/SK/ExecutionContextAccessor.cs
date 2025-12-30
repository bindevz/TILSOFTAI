using ExecutionContext = TILSOFTAI.Domain.ValueObjects.ExecutionContext;

namespace TILSOFTAI.Orchestration.SK;

public sealed class ExecutionContextAccessor
{
    public ExecutionContext Context { get; set; }
    public string? ConfirmedConfirmationId { get; set; } // parse từ user message
}