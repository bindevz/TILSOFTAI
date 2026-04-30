using Microsoft.Extensions.DependencyInjection;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Application.Artifacts;
using TILSOFTAI.Contracts.Configuration;
using TILSOFTAI.Infrastructure.LocalAi;

namespace TILSOFTAI.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddTilsoftAiInfrastructure(this IServiceCollection services)
    {
        services.AddHttpClient<ILocalAiClient, OpenAICompatibleLocalAiClient>((sp, client) =>
        {
            OpenAICompatibleOptions options = sp.GetRequiredService<TilsoftAiOptions>().Ai.OpenAICompatible;
            if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? baseUri))
                client.BaseAddress = baseUri;
            client.Timeout = TimeSpan.FromSeconds(options.RequestTimeoutSeconds);
        });

        services.AddSingleton<IArtifactContentStore>(sp =>
        {
            string rootPath = sp.GetRequiredService<TilsoftAiOptions>().Artifacts.RootPath;
            return new FileSystemArtifactContentStore(rootPath);
        });

        return services;
    }
}

