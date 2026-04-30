using System.Text.Json;
using System.Text.Json.Nodes;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Application.ContextPackaging;
using TILSOFTAI.Application.Security;
using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Contracts.Tools;

namespace TILSOFTAI.Application.Runs;

public sealed class AiRunOrchestrator(
    ICapabilitySearchService capabilitySearch,
    IAgentBrain agentBrain,
    IToolRuntime toolRuntime,
    IArtifactContentStore artifactContentStore,
    IArtifactRepository artifactRepository,
    ILocalAiClient aiClient,
    IAiRunRepository runRepository,
    FinalAnswerProvenanceValidator provenanceValidator)
{
    public async Task<AiRunResponse> CreateRunAsync(RequestContext context, CreateAiRunRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            throw new ArgumentException("Question is required.", nameof(request));

        Guid runId = Guid.NewGuid();
        await runRepository.CreateRunAsync(context, new RunCreateRequest(runId, request.Question, request.Language, request.DomainHint), cancellationToken);

        try
        {
        IReadOnlyList<CapabilityDescriptor> candidates = await capabilitySearch.SearchAsync(context, request.Question, request.DomainHint, cancellationToken);
        if (candidates.Count == 0)
        {
            FinalAnswer answer = StatusAnswer("No Model capability matched the question.", "NoCapabilityFound");
            await runRepository.UpdateRunStatusAsync(context, new RunStatusUpdate(runId, "NoCapabilityFound", null, "NoCapabilityFound", "No registered Model capability matched."), cancellationToken);
            await runRepository.SaveFinalAnswerAsync(context, runId, answer, cancellationToken);
            return new AiRunResponse(runId, "NoCapabilityFound", answer, [], context.RequestTimeUtc);
        }

        AgentPlan plan = await agentBrain.PlanAsync(context, new AgentPlanningInput(request.Question, request.DomainHint, candidates), cancellationToken);
        if (plan.NeedsClarification || plan.SelectedCapability is null)
        {
            FinalAnswer answer = StatusAnswer(plan.Message ?? "More information is required.", "NeedsClarification");
            await runRepository.UpdateRunStatusAsync(context, new RunStatusUpdate(runId, "NeedsClarification", plan.SelectedCapability?.CapabilityCode, "NeedsClarification", plan.Message), cancellationToken);
            await runRepository.SaveFinalAnswerAsync(context, runId, answer, cancellationToken);
            return new AiRunResponse(runId, "NeedsClarification", answer, [], context.RequestTimeUtc);
        }

        await runRepository.UpdateRunStatusAsync(context, new RunStatusUpdate(runId, "Running", plan.SelectedCapability.CapabilityCode), cancellationToken);
        ToolExecutionResult toolResult = await toolRuntime.ExecuteAsync(context, new ToolExecutionRequest(plan.SelectedCapability.Tool, plan.Parameters), cancellationToken);
        await runRepository.RecordToolCallAsync(context, new ToolCallRecord(Guid.NewGuid(), runId, toolResult.ToolName, plan.Parameters, "Completed", toolResult.Rows.Count, (long)toolResult.Elapsed.TotalMilliseconds), cancellationToken);

        ArtifactMetadataResponse rawArtifact = await PersistArtifactAsync(context, runId, "RawResult", JsonSerializer.Serialize(toolResult.Rows), cancellationToken);
        IReadOnlyList<IReadOnlyDictionary<string, object?>> sanitizedRows = SanitizerAndContextPackager.Sanitize(toolResult);
        ArtifactMetadataResponse sanitizedArtifact = await PersistArtifactAsync(context, runId, "SanitizedResult", JsonSerializer.Serialize(sanitizedRows), cancellationToken);
        await artifactRepository.CreateProvenanceAsync(context, new ProvenanceCreateRequest(runId, toolResult.ToolName, toolResult.Filters, sanitizedArtifact.ArtifactId), cancellationToken);

        JsonObject contextPackage = SanitizerAndContextPackager.Build(request.Question, toolResult, sanitizedArtifact.ArtifactId);
        ArtifactMetadataResponse contextArtifact = await PersistArtifactAsync(context, runId, "ContextPackage", contextPackage.ToJsonString(), cancellationToken);

        AiChatResponse aiResponse = await aiClient.ChatAsync(new AiChatRequest("system-answer-generation.v1", request.Question, contextPackage), cancellationToken);
        FinalAnswer finalAnswer = provenanceValidator.ValidateAndAttachSystemProvenance(aiResponse.Answer, toolResult, sanitizedArtifact.ArtifactId);
        ArtifactMetadataResponse finalAnswerArtifact = await PersistArtifactAsync(context, runId, "FinalAnswer", JsonSerializer.Serialize(finalAnswer), cancellationToken);

        await runRepository.SaveFinalAnswerAsync(context, runId, finalAnswer, cancellationToken);
        await runRepository.UpdateRunStatusAsync(context, new RunStatusUpdate(runId, "Completed", plan.SelectedCapability.CapabilityCode), cancellationToken);

        return new AiRunResponse(runId, "Completed", finalAnswer, [rawArtifact.ArtifactId, sanitizedArtifact.ArtifactId, contextArtifact.ArtifactId, finalAnswerArtifact.ArtifactId], context.RequestTimeUtc);
        }
        catch (Exception ex) when (ex is PermissionDeniedException or UnauthorizedAccessException)
        {
            FinalAnswer answer = StatusAnswer("The requesting user is not authorized to execute this Model capability.", "Forbidden");
            await runRepository.UpdateRunStatusAsync(context, new RunStatusUpdate(runId, "Forbidden", null, "Forbidden", "Permission denied."), cancellationToken);
            await runRepository.SaveFinalAnswerAsync(context, runId, answer, cancellationToken);
            return new AiRunResponse(runId, "Forbidden", answer, [], context.RequestTimeUtc);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            FinalAnswer answer = StatusAnswer("The Model run could not be completed safely.", "Failed");
            await runRepository.UpdateRunStatusAsync(context, new RunStatusUpdate(runId, "Failed", null, "Failed", ex.Message), cancellationToken);
            await runRepository.SaveFinalAnswerAsync(context, runId, answer, cancellationToken);
            return new AiRunResponse(runId, "Failed", answer, [], context.RequestTimeUtc);
        }
    }

    public async Task<RunDetailsResponse?> GetRunAsync(RequestContext context, Guid runId, CancellationToken cancellationToken)
    {
        RunState? run = await runRepository.GetAsync(context, runId, cancellationToken);
        if (run is null)
            return null;

        return new RunDetailsResponse(run.RunId, run.Status, run.SelectedCapability, run.ToolCalls, run.Artifacts, run.Answer ?? StatusAnswer("Final answer is not available.", "Unavailable"), run.CreatedAtUtc);
    }

    public Task<ArtifactMetadataResponse?> GetArtifactMetadataAsync(RequestContext context, Guid artifactId, CancellationToken cancellationToken) =>
        artifactRepository.GetAsync(context, artifactId, cancellationToken);

    private async Task<ArtifactMetadataResponse> PersistArtifactAsync(RequestContext context, Guid runId, string artifactType, string json, CancellationToken cancellationToken)
    {
        Guid artifactId = Guid.NewGuid();
        ArtifactContentWriteResult content = await artifactContentStore.WriteAsync(context, new ArtifactContentWriteRequest(runId, artifactId, artifactType, json), cancellationToken);
        return await artifactRepository.CreateAsync(context, new ArtifactMetadataCreateRequest(artifactId, runId, artifactType, "application/json", content.Path, content.Sha256, content.SizeBytes), cancellationToken);
    }

    private static FinalAnswer StatusAnswer(string summary, string caveat) =>
        new(summary, [], [], [caveat], [], []);
}
