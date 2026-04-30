namespace TILSOFTAI.Contracts.Api;

public sealed record CreateAiRunRequest(string Question, string? Language, string? DomainHint);

public sealed record AiRunResponse(
    Guid RunId,
    string Status,
    FinalAnswer Answer,
    IReadOnlyList<Guid> ArtifactIds,
    DateTimeOffset CreatedAtUtc);

public sealed record RunDetailsResponse(
    Guid RunId,
    string Status,
    string? SelectedCapability,
    IReadOnlyList<ToolCallSummary> ToolCalls,
    IReadOnlyList<ArtifactMetadataResponse> Artifacts,
    FinalAnswer Answer,
    DateTimeOffset CreatedAtUtc);

public sealed record ToolCallSummary(string ToolName, string Status, int RowCount, long ElapsedMilliseconds);

public sealed record ArtifactMetadataResponse(
    Guid ArtifactId,
    Guid RunId,
    string ArtifactType,
    string ContentType,
    string Sha256,
    long SizeBytes,
    DateTimeOffset CreatedAtUtc);

public sealed record FinalAnswer(
    string Summary,
    IReadOnlyList<AnswerTable> Tables,
    IReadOnlyList<string> Insights,
    IReadOnlyList<string> Caveats,
    IReadOnlyList<AnswerProvenance> Provenance,
    IReadOnlyList<string> FollowUps);

public sealed record AnswerTable(string Title, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed record AnswerProvenance(string ToolName, IReadOnlyList<string> Filters, Guid ArtifactId);

