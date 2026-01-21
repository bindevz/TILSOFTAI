using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Modules.Analytics;

public sealed class AnalyticsToolRegistrationProvider : IToolRegistrationProvider
{
    private readonly DynamicIntentValidator _validator;
    private readonly ToolInputSpecCatalog _specCatalog;

    public AnalyticsToolRegistrationProvider(DynamicIntentValidator validator, ToolInputSpecCatalog specCatalog)
    {
        _validator = validator;
        _specCatalog = specCatalog;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return Dynamic("analytics.run", requiresWrite: false);
        yield return Dynamic("atomic.query.execute", requiresWrite: false);
        yield return Dynamic("atomic.catalog.search", requiresWrite: false);
    }

    private ToolDefinition Dynamic(string toolName, bool requiresWrite)
    {
        if (!_specCatalog.TryGet(toolName, out var spec))
            throw new InvalidOperationException($"ToolInputSpec not found for '{toolName}'.");

        var allowedSet = spec.AllowedArgumentNames;
        return new ToolDefinition(
            Name: toolName,
            Validator: args => _validator.Validate(toolName, args).ToObject(),
            RequiresWrite: requiresWrite,
            AllowedArguments: allowedSet);
    }
}
