using Microsoft.Extensions.DependencyInjection;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Application.Artifacts;
using TILSOFTAI.Application.Runs;

namespace TILSOFTAI.Application.Testing;

public static class TestingServiceCollectionExtensions
{
    public static IServiceCollection AddTilsoftAiTestingServices(this IServiceCollection services, string artifactRoot)
    {
        services.AddScoped<IRequestContextAccessor, RequestContextAccessor>();
        services.AddSingleton<TestingRunRepository>();
        services.AddSingleton<IAiRunRepository>(sp => sp.GetRequiredService<TestingRunRepository>());
        services.AddSingleton<IArtifactRepository, TestingArtifactRepository>();
        services.AddSingleton<IArtifactContentStore>(_ => new FileSystemArtifactContentStore(artifactRoot));
        services.AddSingleton<ICapabilitySearchService, TestingCapabilitySearchService>();
        services.AddSingleton<IToolRuntime, TestingToolRuntime>();
        services.AddSingleton<ILocalAiClient, TestingLocalAiClient>();
        services.AddSingleton<IModelParameterBinder, ModelParameterBinder>();
        services.AddSingleton<IAgentBrain, TestingAgentBrain>();
        services.AddSingleton<FinalAnswerProvenanceValidator>();
        services.AddSingleton<AiRunOrchestrator>();
        return services;
    }
}
