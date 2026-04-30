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

public interface IArtifactStore
{
    Task<ArtifactWriteResult> WriteAsync(RequestContext context, ArtifactWriteRequest request, CancellationToken cancellationToken);
    Task<ArtifactReadResult> ReadAsync(RequestContext context, Guid artifactId, CancellationToken cancellationToken);
}

public interface ILocalAiClient
{
    Task<AiChatResponse> ChatAsync(AiChatRequest request, CancellationToken cancellationToken);
    Task<AiEmbeddingResponse> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken);
}

public interface IAiRunRepository
{
    Task SaveAsync(RunState run, CancellationToken cancellationToken);
    Task<RunState?> GetAsync(Guid tenantId, Guid runId, CancellationToken cancellationToken);
    Task<ArtifactMetadataResponse?> GetArtifactAsync(Guid tenantId, Guid artifactId, CancellationToken cancellationToken);
}

public sealed record ArtifactWriteRequest(Guid RunId, string ArtifactType, string ContentType, string JsonContent);
public sealed record ArtifactWriteResult(Guid ArtifactId, ArtifactMetadataResponse Metadata, string Path);
public sealed record ArtifactReadResult(ArtifactMetadataResponse Metadata, string JsonContent);
public sealed record AiChatRequest(string SystemPrompt, string UserPrompt, JsonObject ContextPackage);
public sealed record AiChatResponse(FinalAnswer Answer);
public sealed record AiEmbeddingRequest(string Input);
public sealed record AiEmbeddingResponse(float[] Vector, string Model);

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
    public FinalAnswer? Answer { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

