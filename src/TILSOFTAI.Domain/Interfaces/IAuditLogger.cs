using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

public interface IAuditLogger
{
    Task LogUserInputAsync(ValueObjects.TSExecutionContext context, string input, CancellationToken cancellationToken);
    Task LogAiDecisionAsync(ValueObjects.TSExecutionContext context, string aiOutput, CancellationToken cancellationToken);
    Task LogToolExecutionAsync(ValueObjects.TSExecutionContext context, string toolName, object arguments, object result, CancellationToken cancellationToken);
}
