using Microsoft.Extensions.DependencyInjection;
using TILSOFTAI.Orchestration.Modules.DocumentSearch;
using TILSOFTAI.Orchestration.Modules.DocumentSearch.Handlers;
using TILSOFTAI.Orchestration.Modules.EntityGraph;
using TILSOFTAI.Orchestration.Modules.EntityGraph.Handlers;
using TILSOFTAI.Orchestration.Tools.Modularity;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration;

public static class DependencyInjection
{
    public static IServiceCollection AddEntityGraphOrchestration(this IServiceCollection services)
    {
        // Tool Schemas
        services.AddSingleton<IToolInputSpecProvider, EntityGraphToolInputSpecProvider>();

        // Tool Registration (Allowlist logic)
        services.AddSingleton<IToolRegistrationProvider, EntityGraphToolRegistrationProvider>();

        // Handlers
        services.AddScoped<IToolHandler, EntityGraphSearchToolHandler>();
        services.AddScoped<IToolHandler, EntityGraphGetToolHandler>();

        return services;
    }

    public static IServiceCollection AddDocumentSearchOrchestration(this IServiceCollection services)
    {
        // Tool Schemas
        services.AddSingleton<IToolInputSpecProvider, DocumentSearchToolInputSpecProvider>();

        // Tool Registration (Allowlist logic)
        services.AddSingleton<IToolRegistrationProvider, DocumentSearchToolRegistrationProvider>();

        // Handlers
        services.AddScoped<IToolHandler, DocumentSearchToolHandler>();

        return services;
    }
}
