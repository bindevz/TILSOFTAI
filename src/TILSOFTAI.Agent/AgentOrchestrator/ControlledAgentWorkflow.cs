using System.Text.Json.Nodes;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Contracts.Tools;

namespace TILSOFTAI.Agent.AgentOrchestrator;

public sealed class ControlledAgentWorkflow(ICapabilitySearchService capabilitySearch)
{
    public async Task<(CapabilityDescriptor Capability, JsonObject Parameters)> PlanAsync(
        RequestContext context,
        string question,
        string? domainHint,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<CapabilityDescriptor> capabilities = await capabilitySearch.SearchAsync(context, question, domainHint, cancellationToken);
        CapabilityDescriptor selected = capabilities.FirstOrDefault() ?? throw new InvalidOperationException("No shortlisted Model capability is available.");
        return (selected, new JsonObject { ["projectCode"] = ExtractProjectCode(question) });
    }

    private static string ExtractProjectCode(string question)
    {
        string token = question.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(part => part.StartsWith("MODEL-", StringComparison.OrdinalIgnoreCase)) ?? "MODEL-001";
        return token.TrimEnd('.', '?', ',', ';', ':').ToUpperInvariant();
    }
}

