using TILSOFTAI.Agent.AgentFramework;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Application.Runs;
using TILSOFTAI.Application.Testing;
using TILSOFTAI.Contracts.Configuration;
using TILSOFTAI.Infrastructure.DependencyInjection;
using TILSOFTAI.Persistence.DependencyInjection;

namespace TILSOFTAI.Api.DependencyInjection;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddTilsoftAiRuntime(this IServiceCollection services, IWebHostEnvironment environment, TilsoftAiOptions options)
    {
        services.AddSingleton(options);
        if (environment.IsEnvironment("Testing"))
        {
            string root = string.IsNullOrWhiteSpace(options.Artifacts.RootPath)
                ? Path.Combine(AppContext.BaseDirectory, "testing-artifacts")
                : options.Artifacts.RootPath;
            services.AddTilsoftAiTestingServices(root);
            return services;
        }

        services.AddScoped<IRequestContextAccessor, RequestContextAccessor>();
        services.AddSingleton<FinalAnswerProvenanceValidator>();
        services.AddTilsoftAiPersistence();
        services.AddTilsoftAiInfrastructure();
        services.AddTilsoftAiAgentFramework();
        services.AddSingleton<AiRunOrchestrator>();
        return services;
    }
}

