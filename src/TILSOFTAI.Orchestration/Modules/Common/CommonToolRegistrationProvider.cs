using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Modules.Common;

public sealed class CommonToolRegistrationProvider : IToolRegistrationProvider
{
    private readonly DynamicIntentValidator _validator;

    public CommonToolRegistrationProvider(DynamicIntentValidator validator)
    {
        _validator = validator;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return Dynamic("filters.catalog", requiresWrite: false, allowed: new[] { "resource", "includeValues" });
        yield return Dynamic("actions.catalog", requiresWrite: false, allowed: new[] { "action", "includeExamples" });
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
