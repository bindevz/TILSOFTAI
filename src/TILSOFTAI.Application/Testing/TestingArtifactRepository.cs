using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Common;

namespace TILSOFTAI.Application.Testing;

public sealed class TestingArtifactRepository(TestingRunRepository runRepository) : IArtifactRepository
{
    private readonly Dictionary<(Guid TenantId, Guid ArtifactId), ArtifactMetadataResponse> _artifacts = [];

    public Task<ArtifactMetadataResponse> CreateAsync(RequestContext context, ArtifactMetadataCreateRequest request, CancellationToken cancellationToken)
    {
        ArtifactMetadataResponse metadata = new(request.ArtifactId, request.RunId, request.ArtifactType, request.ContentType, request.Sha256, request.SizeBytes, DateTimeOffset.UtcNow);
        _artifacts[(context.TenantId, metadata.ArtifactId)] = metadata;
        runRepository.AddArtifact(context.TenantId, request.RunId, metadata);
        return Task.FromResult(metadata);
    }

    public Task<ArtifactMetadataResponse?> GetAsync(RequestContext context, Guid artifactId, CancellationToken cancellationToken)
    {
        _artifacts.TryGetValue((context.TenantId, artifactId), out ArtifactMetadataResponse? metadata);
        return Task.FromResult(metadata);
    }

    public Task CreateProvenanceAsync(RequestContext context, ProvenanceCreateRequest request, CancellationToken cancellationToken)
    {
        runRepository.AddProvenance(context.TenantId, request.RunId, new AnswerProvenance(request.ToolName, request.Filters, request.ArtifactId));
        return Task.CompletedTask;
    }
}

