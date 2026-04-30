using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Contracts.Tools;

namespace TILSOFTAI.Application.Testing;

public sealed class TestingCapabilitySearchService : ICapabilitySearchService
{
    private static readonly Guid VerifyCapabilityId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid LatestCapabilityId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid FailedCapabilityId = Guid.Parse("10000000-0000-0000-0000-000000000003");

    private static readonly IReadOnlyList<CapabilityDescriptor> Capabilities =
    [
        Create(VerifyCapabilityId, "model.project.run.verify", "Verify Model project run", "Determines whether a Model project achieved the run target.", "Model.GetProjectRunVerification", "model.usp_GetProjectRunVerification"),
        Create(LatestCapabilityId, "model.project.run.latest", "Latest Model project run", "Summarizes latest run status for a Model project.", "Model.GetLatestProjectRun", "model.usp_GetLatestProjectRun"),
        Create(FailedCapabilityId, "model.project.run.failed_checks", "Failed Model checks", "Lists failed checks in the latest Model project run.", "Model.GetFailedRunChecks", "model.usp_GetFailedRunChecks")
    ];

    public Task<IReadOnlyList<CapabilityDescriptor>> SearchAsync(RequestContext context, string question, string? domainHint, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(domainHint) && !string.Equals(domainHint, "Model", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<IReadOnlyList<CapabilityDescriptor>>([]);

        string normalized = question.ToLowerInvariant();
        IEnumerable<CapabilityDescriptor> ordered = normalized.Contains("failed") || normalized.Contains("what failed")
            ? Capabilities.Where(c => c.CapabilityCode.EndsWith("failed_checks", StringComparison.Ordinal))
            : normalized.Contains("latest") || normalized.Contains("status")
                ? Capabilities.Where(c => c.CapabilityCode.EndsWith("latest", StringComparison.Ordinal))
                : Capabilities.Where(c => c.CapabilityCode.EndsWith("verify", StringComparison.Ordinal));

        return Task.FromResult<IReadOnlyList<CapabilityDescriptor>>(ordered.ToList());
    }

    private static CapabilityDescriptor Create(Guid id, string code, string name, string description, string toolName, string procedure)
    {
        ToolDescriptor tool = new(Guid.NewGuid(), toolName, procedure, "{\"type\":\"object\",\"required\":[\"projectCode\"],\"properties\":{\"projectCode\":{\"type\":\"string\",\"pattern\":\"^MODEL-[0-9]{3}$\"}}}", 5000, 30000, "model.project.run.read");
        return new(id, "Model", code, name, description, "model.project.run.read", tool);
    }
}

