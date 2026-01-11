using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Modules.Analytics;

public sealed class AnalyticsToolRegistrationProvider : IToolRegistrationProvider
{
    private readonly DynamicIntentValidator _validator;

    public AnalyticsToolRegistrationProvider(DynamicIntentValidator validator)
    {
        _validator = validator;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return Dynamic("analytics.run", requiresWrite: false,
            allowed: new[] { "datasetId", "pipeline", "topN", "maxGroups", "maxResultRows" });

        yield return Dynamic("atomic.query.execute", requiresWrite: false,
            allowed: new[] { "spName", "params", "maxRowsPerTable", "maxRowsSummary", "maxSchemaRows", "maxTables", "maxColumns", "maxDisplayRows", "previewRows" });
    }

    private ToolDefinition Dynamic(string toolName, bool requiresWrite, IEnumerable<string> allowed)
    {
        var allowedSet = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        return new ToolDefinition(
            Name: toolName,
            Validator: args => _validator.Validate(toolName, args).ToObject(),
            RequiresWrite: requiresWrite,
            AllowedArguments: allowedSet);
    }
}
