using System.Text.Json;
using Microsoft.Extensions.Logging;
using TILSOFTAI.Domain.Interfaces;

namespace TILSOFTAI.Infrastructure.Observability;

public sealed class AuditLogger : IAuditLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(ILogger<AuditLogger> logger)
    {
        _logger = logger;
    }

    public Task LogUserInputAsync(TILSOFTAI.Domain.ValueObjects.ExecutionContext context, string input, CancellationToken cancellationToken)
    {
        _logger.LogInformation("audit:user_input tenant={TenantId} user={UserId} correlation={CorrelationId} payload={Payload}",
            context.TenantId, context.UserId, context.CorrelationId, input);
        return Task.CompletedTask;
    }

    public Task LogAiDecisionAsync(TILSOFTAI.Domain.ValueObjects.ExecutionContext context, string aiOutput, CancellationToken cancellationToken)
    {
        _logger.LogInformation("audit:ai_decision tenant={TenantId} user={UserId} correlation={CorrelationId} payload={Payload}",
            context.TenantId, context.UserId, context.CorrelationId, aiOutput);
        return Task.CompletedTask;
    }

    public Task LogToolExecutionAsync(TILSOFTAI.Domain.ValueObjects.ExecutionContext context, string toolName, object arguments, object result, CancellationToken cancellationToken)
    {
        var serializedArgs = JsonSerializer.Serialize(arguments, SerializerOptions);
        var serializedResult = JsonSerializer.Serialize(result, SerializerOptions);

        _logger.LogInformation(
            "audit:tool_execution tenant={TenantId} user={UserId} correlation={CorrelationId} tool={Tool} args={Args} result={Result}",
            context.TenantId,
            context.UserId,
            context.CorrelationId,
            toolName,
            serializedArgs,
            serializedResult);

        return Task.CompletedTask;
    }
}
