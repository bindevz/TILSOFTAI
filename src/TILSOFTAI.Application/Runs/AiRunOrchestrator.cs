using System.Text.Json;
using System.Text.Json.Nodes;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Application.ContextPackaging;
using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Contracts.Tools;

namespace TILSOFTAI.Application.Runs;

public sealed class AiRunOrchestrator(
    ICapabilitySearchService capabilitySearch,
    IToolRuntime toolRuntime,
    IArtifactStore artifactStore,
    ILocalAiClient aiClient,
    IAiRunRepository repository)
{
    public async Task<AiRunResponse> CreateRunAsync(RequestContext context, CreateAiRunRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            throw new ArgumentException("Question is required.", nameof(request));

        RunState run = new()
        {
            TenantId = context.TenantId,
            UserId = context.UserId,
            RunId = Guid.NewGuid(),
            Question = request.Question,
            Status = "Running"
        };

        IReadOnlyList<CapabilityDescriptor> candidates = await capabilitySearch.SearchAsync(context, request.Question, request.DomainHint, cancellationToken);
        CapabilityDescriptor selected = candidates.FirstOrDefault() ?? throw new InvalidOperationException("No allowed Model capability matched the question.");
        run.SelectedCapability = selected.CapabilityCode;

        string projectCode = ExtractProjectCode(request.Question);
        JsonObject parameters = new() { ["projectCode"] = projectCode };
        ToolExecutionResult toolResult = await toolRuntime.ExecuteAsync(context, new ToolExecutionRequest(selected.Tool, parameters), cancellationToken);
        run.ToolCalls.Add(new ToolCallSummary(toolResult.ToolName, "Completed", toolResult.Rows.Count, (long)toolResult.Elapsed.TotalMilliseconds));

        string rawJson = JsonSerializer.Serialize(toolResult.Rows);
        ArtifactWriteResult raw = await artifactStore.WriteAsync(context, new ArtifactWriteRequest(run.RunId, "RawResult", "application/json", rawJson), cancellationToken);
        run.Artifacts.Add(raw.Metadata);

        IReadOnlyList<IReadOnlyDictionary<string, object?>> sanitized = SanitizerAndContextPackager.Sanitize(toolResult);
        string sanitizedJson = JsonSerializer.Serialize(sanitized);
        ArtifactWriteResult sanitizedArtifact = await artifactStore.WriteAsync(context, new ArtifactWriteRequest(run.RunId, "SanitizedResult", "application/json", sanitizedJson), cancellationToken);
        run.Artifacts.Add(sanitizedArtifact.Metadata);

        JsonObject contextPackage = SanitizerAndContextPackager.Build(request.Question, toolResult, sanitizedArtifact.ArtifactId);
        ArtifactWriteResult packageArtifact = await artifactStore.WriteAsync(context, new ArtifactWriteRequest(run.RunId, "ContextPackage", "application/json", contextPackage.ToJsonString()), cancellationToken);
        run.Artifacts.Add(packageArtifact.Metadata);

        AiChatResponse response = await aiClient.ChatAsync(new AiChatRequest("system-answer-generation.v1", request.Question, contextPackage), cancellationToken);
        run.Answer = response.Answer;
        run.Status = "Completed";
        await repository.SaveAsync(run, cancellationToken);

        return new AiRunResponse(run.RunId, run.Status, run.Answer, run.Artifacts.Select(a => a.ArtifactId).ToList(), run.CreatedAtUtc);
    }

    public async Task<RunDetailsResponse?> GetRunAsync(RequestContext context, Guid runId, CancellationToken cancellationToken)
    {
        RunState? run = await repository.GetAsync(context.TenantId, runId, cancellationToken);
        if (run is null || run.UserId != context.UserId)
            return null;

        return new RunDetailsResponse(run.RunId, run.Status, run.SelectedCapability, run.ToolCalls, run.Artifacts, run.Answer!, run.CreatedAtUtc);
    }

    public Task<ArtifactMetadataResponse?> GetArtifactMetadataAsync(RequestContext context, Guid artifactId, CancellationToken cancellationToken) =>
        repository.GetArtifactAsync(context.TenantId, artifactId, cancellationToken);

    public static string ExtractProjectCode(string question)
    {
        string token = question.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith("MODEL-", StringComparison.OrdinalIgnoreCase)) ?? "MODEL-001";
        return token.TrimEnd('.', '?', ',', ';', ':').ToUpperInvariant();
    }
}

