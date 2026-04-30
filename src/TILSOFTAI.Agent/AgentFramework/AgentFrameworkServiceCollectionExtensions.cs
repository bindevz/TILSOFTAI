using Microsoft.Extensions.DependencyInjection;
using TILSOFTAI.Agent.Model;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Application.Runs;

namespace TILSOFTAI.Agent.AgentFramework;

public static class AgentFrameworkServiceCollectionExtensions
{
    public static IServiceCollection AddTilsoftAiAgentFramework(this IServiceCollection services)
    {
        services.AddSingleton<IModelParameterBinder, ModelParameterBinder>();
        services.AddSingleton<ModelRunAnalysisWorkflow>();
        services.AddSingleton<IAgentBrain, AgentFrameworkBrain>();
        return services;
    }
}
