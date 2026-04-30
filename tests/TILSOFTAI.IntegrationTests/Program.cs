using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Application.Artifacts;
using TILSOFTAI.Application.Capabilities;
using TILSOFTAI.Application.LocalAi;
using TILSOFTAI.Application.Runs;
using TILSOFTAI.Application.Tools;
using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.IntegrationTests;

string artifactRoot = Path.Combine(Path.GetTempPath(), "tilsoftai-integration-artifacts", Guid.NewGuid().ToString("D"));
IAiRunRepository repository = new InMemoryRunRepository();
AiRunOrchestrator orchestrator = new(
    new InMemoryCapabilitySearchService(),
    new ModelToolRuntime(),
    new FileSystemArtifactStore(artifactRoot, repository),
    new DeterministicLocalAiClient(),
    repository);

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

RequestContext unauthorizedContext = validContext with { UserId = Guid.Parse("00000000-0000-0000-0000-000000000102") };
await IntegrationTestAssert.ThrowsAsync<UnauthorizedAccessException>(() =>
    orchestrator.CreateRunAsync(unauthorizedContext, new CreateAiRunRequest("Did MODEL-001 achieve its run target?", "en", "Model"), CancellationToken.None));

Console.WriteLine("Integration tests passed.");
