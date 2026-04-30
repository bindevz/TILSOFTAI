using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Application.Runs;
using TILSOFTAI.Application.Testing;
using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.IntegrationTests;

string artifactRoot = Path.Combine(Path.GetTempPath(), "tilsoftai-integration-artifacts", Guid.NewGuid().ToString("D"));
TestingRunRepository repository = new();
AiRunOrchestrator orchestrator = new(
    new TestingCapabilitySearchService(),
    new TestingAgentBrain(new ModelParameterBinder()),
    new TestingToolRuntime(),
    new TILSOFTAI.Application.Artifacts.FileSystemArtifactContentStore(artifactRoot),
    new TestingArtifactRepository(repository),
    new TestingLocalAiClient(),
    repository,
    new FinalAnswerProvenanceValidator());

RequestContext validContext = new(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000101"), Guid.NewGuid().ToString("D"), "en", DateTimeOffset.UtcNow);
AiRunResponse run = await orchestrator.CreateRunAsync(validContext, new CreateAiRunRequest("Did MODEL-001 achieve its run target? Show evidence and follow-up checks.", "en", "Model"), CancellationToken.None);

IntegrationTestAssert.Equal("Completed", run.Status);
IntegrationTestAssert.True(run.ArtifactIds.Count >= 3, "Run must create raw, sanitized, and context artifacts.");
IntegrationTestAssert.True(run.Answer.Provenance.Count > 0, "Final answer must include provenance.");
IntegrationTestAssert.True(run.Answer.Tables.Count > 0, "Final answer must include a table.");
IntegrationTestAssert.True(run.Answer.Insights.Count > 0, "Final answer must include insight.");
IntegrationTestAssert.True(run.Answer.FollowUps.Count > 0, "Final answer must include follow-ups.");

RunDetailsResponse? details = await orchestrator.GetRunAsync(validContext, run.RunId, CancellationToken.None);
IntegrationTestAssert.True(details is not null, "Run should be retrievable by tenant/user.");

AiRunResponse viRun = await orchestrator.CreateRunAsync(validContext, new CreateAiRunRequest("MODEL-001 có đạt mục tiêu run không?", "vi", "Model"), CancellationToken.None);
IntegrationTestAssert.Equal("Completed", viRun.Status);

AiRunResponse latestRun = await orchestrator.CreateRunAsync(validContext, new CreateAiRunRequest("Show latest status for MODEL-002.", "en", "Model"), CancellationToken.None);
IntegrationTestAssert.Equal("Completed", latestRun.Status);

AiRunResponse failedRun = await orchestrator.CreateRunAsync(validContext, new CreateAiRunRequest("Which checks failed for MODEL-001?", "en", "Model"), CancellationToken.None);
IntegrationTestAssert.Equal("Completed", failedRun.Status);

AiRunResponse clarification = await orchestrator.CreateRunAsync(validContext, new CreateAiRunRequest("Did the project achieve its run target?", "en", "Model"), CancellationToken.None);
IntegrationTestAssert.Equal("NeedsClarification", clarification.Status);

AiRunResponse invalidProject = await orchestrator.CreateRunAsync(validContext, new CreateAiRunRequest("Did MODEL-XYZ achieve its run target?", "en", "Model"), CancellationToken.None);
IntegrationTestAssert.Equal("NeedsClarification", invalidProject.Status);

RequestContext unauthorizedContext = validContext with { UserId = Guid.Parse("00000000-0000-0000-0000-000000000102") };
AiRunResponse forbidden = await orchestrator.CreateRunAsync(unauthorizedContext, new CreateAiRunRequest("Did MODEL-001 achieve its run target?", "en", "Model"), CancellationToken.None);
IntegrationTestAssert.Equal("Forbidden", forbidden.Status);

RequestContext otherTenant = validContext with { TenantId = Guid.Parse("00000000-0000-0000-0000-000000000002") };
ArtifactMetadataResponse? wrongTenantArtifact = await orchestrator.GetArtifactMetadataAsync(otherTenant, run.ArtifactIds[0], CancellationToken.None);
IntegrationTestAssert.True(wrongTenantArtifact is null, "Wrong-tenant artifact metadata must not be visible.");

Console.WriteLine("Integration tests passed.");
