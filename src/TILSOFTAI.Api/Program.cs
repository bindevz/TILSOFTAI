using TILSOFTAI.Api.Middleware;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Application.Artifacts;
using TILSOFTAI.Application.Capabilities;
using TILSOFTAI.Application.LocalAi;
using TILSOFTAI.Application.Runs;
using TILSOFTAI.Application.Security;
using TILSOFTAI.Application.Tools;
using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Configuration;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

TilsoftAiOptions options = new();
builder.Configuration.Bind(options);
IReadOnlyList<string> validationErrors = ConfigurationValidator.Validate(options);
if (validationErrors.Count > 0 && !builder.Environment.IsEnvironment("Testing"))
    throw new InvalidOperationException("Invalid TILSOFTAI configuration: " + string.Join(" ", validationErrors));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<IRequestContextAccessor, RequestContextAccessor>();
builder.Services.AddSingleton<IAiRunRepository, InMemoryRunRepository>();
builder.Services.AddSingleton<ICapabilitySearchService, InMemoryCapabilitySearchService>();
builder.Services.AddSingleton<IToolRuntime, ModelToolRuntime>();
builder.Services.AddSingleton<ILocalAiClient, DeterministicLocalAiClient>();
builder.Services.AddSingleton<IArtifactStore>(sp =>
{
    string configuredRoot = sp.GetRequiredService<TilsoftAiOptions>().Artifacts.RootPath;
    string root = string.IsNullOrWhiteSpace(configuredRoot)
        ? Path.Combine(AppContext.BaseDirectory, "artifacts")
        : configuredRoot;
    return new FileSystemArtifactStore(root, sp.GetRequiredService<IAiRunRepository>());
});
builder.Services.AddSingleton<AiRunOrchestrator>();
builder.Services.AddEndpointsApiExplorer();

WebApplication app = builder.Build();

app.UseMiddleware<RequestContextMiddleware>();

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
    .WithName("LiveHealth");

app.MapGet("/health/ready", (TilsoftAiOptions readyOptions) =>
{
    IReadOnlyList<string> errors = ConfigurationValidator.Validate(readyOptions);
    return errors.Count == 0
        ? Results.Ok(new { status = "Healthy" })
        : Results.Problem(string.Join(" ", errors), statusCode: StatusCodes.Status503ServiceUnavailable);
}).WithName("ReadyHealth");

app.MapPost("/api/v1/ai/runs", async (
    CreateAiRunRequest request,
    IRequestContextAccessor contextAccessor,
    AiRunOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    if (contextAccessor.Current is null)
        return Results.Problem("Request context is unavailable.", statusCode: StatusCodes.Status500InternalServerError);

    AiRunResponse response = await orchestrator.CreateRunAsync(contextAccessor.Current, request, cancellationToken);
    return Results.Ok(response);
}).WithName("CreateAiRun");

app.MapGet("/api/v1/ai/runs/{runId:guid}", async (
    Guid runId,
    IRequestContextAccessor contextAccessor,
    AiRunOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    if (contextAccessor.Current is null)
        return Results.Problem("Request context is unavailable.", statusCode: StatusCodes.Status500InternalServerError);

    RunDetailsResponse? response = await orchestrator.GetRunAsync(contextAccessor.Current, runId, cancellationToken);
    return response is null ? Results.NotFound() : Results.Ok(response);
}).WithName("GetAiRun");

app.MapGet("/api/v1/ai/artifacts/{artifactId:guid}", async (
    Guid artifactId,
    IRequestContextAccessor contextAccessor,
    AiRunOrchestrator orchestrator,
    CancellationToken cancellationToken) =>
{
    if (contextAccessor.Current is null)
        return Results.Problem("Request context is unavailable.", statusCode: StatusCodes.Status500InternalServerError);

    ArtifactMetadataResponse? response = await orchestrator.GetArtifactMetadataAsync(contextAccessor.Current, artifactId, cancellationToken);
    return response is null ? Results.NotFound() : Results.Ok(response);
}).WithName("GetArtifactMetadata");

app.Run();

public partial class Program;

