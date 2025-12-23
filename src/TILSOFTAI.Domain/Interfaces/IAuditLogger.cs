using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

public interface IAuditLogger
{
    Task LogUserInputAsync(ValueObjects.ExecutionContext context, string input, CancellationToken cancellationToken);
    Task LogAiDecisionAsync(ValueObjects.ExecutionContext context, string aiOutput, CancellationToken cancellationToken);
    Task LogToolExecutionAsync(ValueObjects.ExecutionContext context, string toolName, object arguments, object result, CancellationToken cancellationToken);
}
