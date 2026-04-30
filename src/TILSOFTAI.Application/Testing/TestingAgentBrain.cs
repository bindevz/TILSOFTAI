using System.Text.Json.Nodes;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Common;

namespace TILSOFTAI.Application.Testing;

public sealed class TestingAgentBrain(IModelParameterBinder parameterBinder) : IAgentBrain
{
    public Task<AgentPlan> PlanAsync(RequestContext context, AgentPlanningInput input, CancellationToken cancellationToken)
    {
        if (input.Candidates.Count == 0)
            return Task.FromResult(new AgentPlan(null, [], false, "No Model capability matched."));

        ParameterBindingResult binding = parameterBinder.BindProjectCode(input.Question);
        if (!binding.Success)
            return Task.FromResult(new AgentPlan(input.Candidates[0], [], true, binding.Message));

        string normalized = input.Question.ToLowerInvariant();
        var selected = normalized.Contains("failed") || normalized.Contains("which checks")
            ? input.Candidates.FirstOrDefault(c => c.CapabilityCode.EndsWith("failed_checks", StringComparison.Ordinal)) ?? input.Candidates[0]
            : normalized.Contains("latest") || normalized.Contains("status")
                ? input.Candidates.FirstOrDefault(c => c.CapabilityCode.EndsWith("latest", StringComparison.Ordinal)) ?? input.Candidates[0]
                : input.Candidates.FirstOrDefault(c => c.CapabilityCode.EndsWith("verify", StringComparison.Ordinal)) ?? input.Candidates[0];

        return Task.FromResult(new AgentPlan(selected, new JsonObject { ["projectCode"] = binding.ProjectCode }, false, null));
    }
}

