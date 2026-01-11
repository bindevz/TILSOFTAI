using System.Reflection;
using Scrutor;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Infrastructure.Repositories;

namespace TILSOFTAI.Api.DependencyInjection;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddTilsoftaiAutoRegistrations(this IServiceCollection services)
    {
        // Select assemblies that contain Application Services / Infrastructure Repositories.
        // Ver26.4: Remove model-specific repositories; keep only generic AtomicQuery + shared services.
        Assembly appAsm = typeof(AtomicQueryService).Assembly;          // TILSOFTAI.Application
        Assembly infraAsm = typeof(AtomicQueryRepository).Assembly;     // TILSOFTAI.Infrastructure

        services.Scan(scan => scan
            .FromAssemblies(appAsm, infraAsm)

            // -------- Application Services --------
            .AddClasses(c => c
                .InNamespaceOf<AtomicQueryService>()
                .Where(t => t.Name.EndsWith("Service", StringComparison.Ordinal)))
            .UsingRegistrationStrategy(RegistrationStrategy.Throw)
            .AsSelf()
            .WithScopedLifetime()

            // -------- Infrastructure Repositories --------
            .AddClasses(c => c
                .InNamespaceOf<AtomicQueryRepository>()
                .Where(t => t.Name.EndsWith("Repository", StringComparison.Ordinal)))
            .UsingRegistrationStrategy(RegistrationStrategy.Throw)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
        );

        return services;
    }
}
