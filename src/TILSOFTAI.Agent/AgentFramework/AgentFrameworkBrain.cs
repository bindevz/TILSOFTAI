using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Agent.Model;

namespace TILSOFTAI.Agent.AgentFramework;

public sealed class AgentFrameworkBrain(ModelRunAnalysisWorkflow workflow) : IAgentBrain
{
    public Task<AgentPlan> PlanAsync(RequestContext context, AgentPlanningInput input, CancellationToken cancellationToken) =>
        workflow.PlanAsync(context, input, cancellationToken);
}

