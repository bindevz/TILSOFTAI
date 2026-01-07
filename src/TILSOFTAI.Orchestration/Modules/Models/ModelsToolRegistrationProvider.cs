using System.Text.Json;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Modules.Models;

public sealed class ModelsToolRegistrationProvider : IToolRegistrationProvider
{
    private readonly DynamicIntentValidator _validator;

    public ModelsToolRegistrationProvider(DynamicIntentValidator validator)
    {
        _validator = validator;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        yield return Dynamic("models.search", requiresWrite: false, allowed: new[] { "filters", "page", "pageSize" });
        yield return Dynamic("models.count", requiresWrite: false, allowed: new[] { "filters" });
        yield return Dynamic("models.stats", requiresWrite: false, allowed: new[] { "filters", "topN" });
        yield return Dynamic("models.options", requiresWrite: false, allowed: new[] { "modelId", "includeConstraints" });
        yield return Dynamic("models.get", requiresWrite: false, allowed: new[] { "modelId" });
        yield return Dynamic("models.attributes.list", requiresWrite: false, allowed: new[] { "modelId" });
        yield return Dynamic("models.price.analyze", requiresWrite: false, allowed: new[] { "modelId" });
        yield return Dynamic("models.create.prepare", requiresWrite: true, allowed: new[] { "name", "category", "basePrice", "attributes" });
        yield return Dynamic("models.create.commit", requiresWrite: true, allowed: new[] { "confirmationId" });
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
