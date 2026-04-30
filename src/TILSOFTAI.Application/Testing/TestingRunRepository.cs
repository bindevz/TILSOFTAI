using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Common;

namespace TILSOFTAI.Application.Testing;

public sealed class TestingRunRepository : IAiRunRepository
{
    private readonly Dictionary<(Guid TenantId, Guid RunId), RunState> _runs = [];

    public Task CreateRunAsync(RequestContext context, RunCreateRequest request, CancellationToken cancellationToken)
    {
        _runs[(context.TenantId, request.RunId)] = new RunState
        {
            TenantId = context.TenantId,
            UserId = context.UserId,
            RunId = request.RunId,
            Question = request.Question,
            Status = "Running",
            CreatedAtUtc = context.RequestTimeUtc
        };
        return Task.CompletedTask;
    }

    public Task UpdateRunStatusAsync(RequestContext context, RunStatusUpdate update, CancellationToken cancellationToken)
    {
        if (_runs.TryGetValue((context.TenantId, update.RunId), out RunState? run))
        {
            run.Status = update.Status;
            run.SelectedCapability = update.SelectedCapabilityCode ?? run.SelectedCapability;
        }

        return Task.CompletedTask;
    }

    public Task RecordToolCallAsync(RequestContext context, ToolCallRecord record, CancellationToken cancellationToken)
    {
        if (_runs.TryGetValue((context.TenantId, record.RunId), out RunState? run))
            run.ToolCalls.Add(new ToolCallSummary(record.ToolName, record.Status, record.RowCount, record.ElapsedMilliseconds));

        return Task.CompletedTask;
    }

    public Task<RunState?> GetAsync(RequestContext context, Guid runId, CancellationToken cancellationToken)
    {
        _runs.TryGetValue((context.TenantId, runId), out RunState? run);
        return Task.FromResult(run?.UserId == context.UserId ? run : null);
    }

    public Task SaveFinalAnswerAsync(RequestContext context, Guid runId, FinalAnswer answer, CancellationToken cancellationToken)
    {
        SetAnswer(context.TenantId, runId, answer);
        return Task.CompletedTask;
    }

    public void AddArtifact(Guid tenantId, Guid runId, ArtifactMetadataResponse metadata)
    {
        if (_runs.TryGetValue((tenantId, runId), out RunState? run))
            run.Artifacts.Add(metadata);
    }

    public void AddProvenance(Guid tenantId, Guid runId, AnswerProvenance provenance)
    {
        if (_runs.TryGetValue((tenantId, runId), out RunState? run))
            run.Provenance.Add(provenance);
    }

    public void SetAnswer(Guid tenantId, Guid runId, FinalAnswer answer)
    {
        if (_runs.TryGetValue((tenantId, runId), out RunState? run))
            run.Answer = answer;
    }
}
