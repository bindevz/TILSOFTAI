using System.Text.Json.Nodes;
using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Contracts.Tools;

namespace TILSOFTAI.Application.Abstractions;

public interface ICapabilitySearchService
{
    Task<IReadOnlyList<CapabilityDescriptor>> SearchAsync(RequestContext context, string question, string? domainHint, CancellationToken cancellationToken);
}

public interface IToolRuntime
{
    Task<ToolExecutionResult> ExecuteAsync(RequestContext context, ToolExecutionRequest request, CancellationToken cancellationToken);
}

public interface IArtifactContentStore
{
    Task<ArtifactContentWriteResult> WriteAsync(RequestContext context, ArtifactContentWriteRequest request, CancellationToken cancellationToken);
    Task<string> ReadAsync(RequestContext context, ArtifactMetadataResponse metadata, CancellationToken cancellationToken);
}

public interface IArtifactRepository
{
    Task<ArtifactMetadataResponse> CreateAsync(RequestContext context, ArtifactMetadataCreateRequest request, CancellationToken cancellationToken);
    Task<ArtifactMetadataResponse?> GetAsync(RequestContext context, Guid artifactId, CancellationToken cancellationToken);
    Task CreateProvenanceAsync(RequestContext context, ProvenanceCreateRequest request, CancellationToken cancellationToken);
}

public interface ILocalAiClient
{
    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken);
    Task<AiEmbeddingResponse> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken);
}

public interface IAiRunRepository
{
    Task CreateRunAsync(RequestContext context, RunCreateRequest request, CancellationToken cancellationToken);
    Task UpdateRunStatusAsync(RequestContext context, RunStatusUpdate update, CancellationToken cancellationToken);
    Task RecordToolCallAsync(RequestContext context, ToolCallRecord record, CancellationToken cancellationToken);
    Task SaveFinalAnswerAsync(RequestContext context, Guid runId, FinalAnswer answer, CancellationToken cancellationToken);
    Task<RunState?> GetAsync(RequestContext context, Guid runId, CancellationToken cancellationToken);
}

public interface IAgentBrain
{
    Task<AgentPlan> PlanAsync(RequestContext context, AgentPlanningInput input, CancellationToken cancellationToken);
}

public interface IModelParameterBinder
{
    ParameterBindingResult BindProjectCode(string question);
}

public sealed record ArtifactContentWriteRequest(Guid RunId, Guid ArtifactId, string ArtifactType, string JsonContent);
public sealed record ArtifactContentWriteResult(string Path, long SizeBytes, string Sha256);
public sealed record ArtifactMetadataCreateRequest(Guid ArtifactId, Guid RunId, string ArtifactType, string ContentType, string StoragePath, string Sha256, long SizeBytes);
public sealed record ProvenanceCreateRequest(Guid RunId, string ToolName, IReadOnlyList<string> Filters, Guid ArtifactId);
public sealed record ArtifactReadResult(ArtifactMetadataResponse Metadata, string JsonContent);
public sealed record AiChatRequest(string SystemPrompt, string UserPrompt, JsonObject ContextPackage);
public sealed record AiChatResponse(FinalAnswer Answer);
public sealed record AiEmbeddingRequest(string Input);
public sealed record AiEmbeddingResponse(float[] Vector, string Model);
public sealed record RunCreateRequest(Guid RunId, string Question, string? Language, string? DomainHint);
public sealed record RunStatusUpdate(Guid RunId, string Status, string? SelectedCapabilityCode, string? DiagnosticCode = null, string? DiagnosticMessage = null);
public sealed record ToolCallRecord(Guid ToolCallId, Guid RunId, string ToolName, JsonObject Parameters, string Status, int RowCount, long ElapsedMilliseconds);
public sealed record AgentPlanningInput(string Question, string? DomainHint, IReadOnlyList<CapabilityDescriptor> Candidates);
public sealed record AgentPlan(CapabilityDescriptor? SelectedCapability, JsonObject Parameters, bool NeedsClarification, string? Message);
public sealed record ParameterBindingResult(bool Success, string? ProjectCode, string? ErrorCode, string? Message);

public sealed class RunState
{
    public required Guid TenantId { get; init; }
    public required Guid UserId { get; init; }
    public required Guid RunId { get; init; }
    public required string Question { get; init; }
    public string Status { get; set; } = "Created";
    public string? SelectedCapability { get; set; }
    public List<ToolCallSummary> ToolCalls { get; } = [];
    public List<ArtifactMetadataResponse> Artifacts { get; } = [];
    public List<AnswerProvenance> Provenance { get; } = [];
    public FinalAnswer? Answer { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
