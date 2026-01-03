using System.Reflection;
using Scrutor;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Infrastructure.Repositories;

namespace TILSOFTAI.Api.DependencyInjection;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddTilsoftaiAutoRegistrations(this IServiceCollection services)
    {
        // Lấy đúng assemblies chứa Services/Repositories
        Assembly appAsm = typeof(ModelsService).Assembly;          // TILSOFTAI.Application
        Assembly infraAsm = typeof(ModelRepository).Assembly;     // TILSOFTAI.Infrastructure

        services.Scan(scan => scan
            .FromAssemblies(appAsm, infraAsm)

            // -------- Application Services --------
            .AddClasses(c => c
                .InNamespaceOf<ModelsService>()
                .Where(t => t.Name.EndsWith("Service")))
            .UsingRegistrationStrategy(RegistrationStrategy.Throw)
            .AsSelf()
            .WithScopedLifetime()

            // -------- Infrastructure Repositories --------
            .AddClasses(c => c
                .InNamespaceOf<ModelRepository>()
                .Where(t => t.Name.EndsWith("Repository")))
            .UsingRegistrationStrategy(RegistrationStrategy.Throw)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
        );

        return services;
    }
}
