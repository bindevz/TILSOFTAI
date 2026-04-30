using System.Text.Json.Nodes;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Persistence.Connection;

namespace TILSOFTAI.Persistence.Runs;

public sealed class SqlAiRunRepository(SqlCommandExecutor executor) : IAiRunRepository
{
    private const int DefaultTimeoutSeconds = 30;

    public Task CreateRunAsync(RequestContext context, RunCreateRequest request, CancellationToken cancellationToken) =>
        executor.ExecuteAsync("ai.usp_CreateRun",
        [
            SqlParameterFactory.UniqueIdentifier("@TenantId", context.TenantId),
            SqlParameterFactory.UniqueIdentifier("@UserId", context.UserId),
            SqlParameterFactory.UniqueIdentifier("@RunId", request.RunId),
            SqlParameterFactory.NVarChar("@CorrelationId", context.CorrelationId, 100),
            SqlParameterFactory.NVarChar("@Question", request.Question),
            SqlParameterFactory.NVarChar("@DetectedLanguage", request.Language, 20),
            SqlParameterFactory.NVarChar("@DomainHint", request.DomainHint, 100)
        ], DefaultTimeoutSeconds, cancellationToken);

    public Task UpdateRunStatusAsync(RequestContext context, RunStatusUpdate update, CancellationToken cancellationToken) =>
        executor.ExecuteAsync("ai.usp_UpdateRunStatus",
        [
            SqlParameterFactory.UniqueIdentifier("@TenantId", context.TenantId),
            SqlParameterFactory.UniqueIdentifier("@UserId", context.UserId),
            SqlParameterFactory.UniqueIdentifier("@RunId", update.RunId),
            SqlParameterFactory.NVarChar("@Status", update.Status, 50),
            SqlParameterFactory.NVarChar("@SelectedCapabilityCode", update.SelectedCapabilityCode, 150),
            SqlParameterFactory.NVarChar("@DiagnosticCode", update.DiagnosticCode, 100),
            SqlParameterFactory.NVarChar("@DiagnosticMessage", update.DiagnosticMessage)
        ], DefaultTimeoutSeconds, cancellationToken);

    public Task RecordToolCallAsync(RequestContext context, ToolCallRecord record, CancellationToken cancellationToken) =>
        executor.ExecuteAsync("ai.usp_RecordToolCallCompletion",
        [
            SqlParameterFactory.UniqueIdentifier("@TenantId", context.TenantId),
            SqlParameterFactory.UniqueIdentifier("@UserId", context.UserId),
            SqlParameterFactory.UniqueIdentifier("@ToolCallId", record.ToolCallId),
            SqlParameterFactory.UniqueIdentifier("@RunId", record.RunId),
            SqlParameterFactory.NVarChar("@ToolName", record.ToolName, 200),
            SqlParameterFactory.NVarChar("@ParametersJson", record.Parameters.ToJsonString()),
            SqlParameterFactory.NVarChar("@Status", record.Status, 50),
            SqlParameterFactory.Int("@RowCount", record.RowCount),
            SqlParameterFactory.BigInt("@ElapsedMilliseconds", record.ElapsedMilliseconds)
        ], DefaultTimeoutSeconds, cancellationToken);

    public Task SaveFinalAnswerAsync(RequestContext context, Guid runId, TILSOFTAI.Contracts.Api.FinalAnswer answer, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<RunState?> GetAsync(RequestContext context, Guid runId, CancellationToken cancellationToken)
    {
        var rows = await executor.QueryRowsAsync("ai.usp_GetRunDetails",
        [
            SqlParameterFactory.UniqueIdentifier("@TenantId", context.TenantId),
            SqlParameterFactory.UniqueIdentifier("@UserId", context.UserId),
            SqlParameterFactory.UniqueIdentifier("@RunId", runId)
        ], DefaultTimeoutSeconds, 5000, cancellationToken);

        var runRow = rows.FirstOrDefault(r => r.TryGetValue("RecordType", out object? recordType) && string.Equals(recordType?.ToString(), "Run", StringComparison.OrdinalIgnoreCase));
        if (runRow is null)
            return null;

        RunState run = new()
        {
            TenantId = context.TenantId,
            UserId = context.UserId,
            RunId = runId,
            Question = runRow.TryGetValue("Question", out object? question) ? question?.ToString() ?? string.Empty : string.Empty,
            Status = runRow.TryGetValue("Status", out object? status) ? status?.ToString() ?? string.Empty : string.Empty,
            SelectedCapability = runRow.TryGetValue("SelectedCapabilityCode", out object? capability) ? capability?.ToString() : null,
            CreatedAtUtc = DateTimeOffset.TryParse(runRow.GetValueOrDefault("CreatedAtUtc")?.ToString(), out DateTimeOffset created) ? created : DateTimeOffset.UtcNow
        };

        foreach (var row in rows.Where(r => string.Equals(r.GetValueOrDefault("RecordType")?.ToString(), "ToolCall", StringComparison.OrdinalIgnoreCase)))
            run.ToolCalls.Add(new(row.GetValueOrDefault("ToolName")?.ToString() ?? string.Empty, row.GetValueOrDefault("ToolStatus")?.ToString() ?? string.Empty, Convert.ToInt32(row.GetValueOrDefault("RowCount") ?? 0), Convert.ToInt64(row.GetValueOrDefault("ElapsedMilliseconds") ?? 0)));

        foreach (var row in rows.Where(r => string.Equals(r.GetValueOrDefault("RecordType")?.ToString(), "Artifact", StringComparison.OrdinalIgnoreCase)))
        {
            if (Guid.TryParse(row.GetValueOrDefault("ArtifactId")?.ToString(), out Guid artifactId))
                run.Artifacts.Add(new(artifactId, runId, row.GetValueOrDefault("ArtifactType")?.ToString() ?? string.Empty, row.GetValueOrDefault("ContentType")?.ToString() ?? string.Empty, row.GetValueOrDefault("Sha256")?.ToString() ?? string.Empty, Convert.ToInt64(row.GetValueOrDefault("SizeBytes") ?? 0), DateTimeOffset.UtcNow));
        }

        return run;
    }
}
