using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Scrutor;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Infrastructure.Repositories;

namespace TILSOFTAI.Api.DependencyInjection;

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddTilsoftaiAutoRegistrations(this IServiceCollection services)
    {
        // Lấy đúng assemblies chứa Services/Repositories
        Assembly appAsm = typeof(OrdersService).Assembly;          // TILSOFTAI.Application
        Assembly infraAsm = typeof(OrdersRepository).Assembly;     // TILSOFTAI.Infrastructure

        services.Scan(scan => scan
            .FromAssemblies(appAsm, infraAsm)

            // -------- Application Services --------
            .AddClasses(c => c
                .InNamespaceOf<OrdersService>()
                .Where(t => t.Name.EndsWith("Service")))
            .UsingRegistrationStrategy(RegistrationStrategy.Throw)
            .AsSelf()
            .WithScopedLifetime()

            // -------- Infrastructure Repositories --------
            .AddClasses(c => c
                .InNamespaceOf<OrdersRepository>()
                .Where(t => t.Name.EndsWith("Repository")))
            .UsingRegistrationStrategy(RegistrationStrategy.Throw)
            .AsImplementedInterfaces()
            .WithScopedLifetime()
        );


        return services;
    }
}
