using Microsoft.Extensions.Options;
using TILSOFTAI.Configuration;

namespace TILSOFTAI.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddAppSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AppSettings>()
            .Bind(configuration)
            .ValidateDataAnnotations()
            .Validate(settings =>
                settings.Localization.SupportedCultures is { Length: > 0 } &&
                settings.Orchestration.ToolAllowlist is { Length: > 0 },
                "Localization.SupportedCultures and Orchestration.ToolAllowlist must be non-empty.")
            .ValidateOnStart();

        return services;
    }
}
