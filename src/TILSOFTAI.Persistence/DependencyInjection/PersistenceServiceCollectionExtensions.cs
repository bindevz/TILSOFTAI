using Microsoft.Extensions.DependencyInjection;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Persistence.Artifacts;
using TILSOFTAI.Persistence.Capabilities;
using TILSOFTAI.Persistence.Connection;
using TILSOFTAI.Persistence.Runs;
using TILSOFTAI.Persistence.Tools;

namespace TILSOFTAI.Persistence.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddTilsoftAiPersistence(this IServiceCollection services)
    {
        services.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        services.AddSingleton<SqlCommandExecutor>();
        services.AddSingleton<IAiRunRepository, SqlAiRunRepository>();
        services.AddSingleton<IArtifactRepository, SqlArtifactRepository>();
        services.AddSingleton<ICapabilitySearchService, SqlCapabilitySearchService>();
        services.AddSingleton<IToolRuntime, SqlModelToolRuntime>();
        return services;
    }
}

