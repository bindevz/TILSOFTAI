using TILSOFTAI.Configuration;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;
using AppSettings = TILSOFTAI.Configuration.AppSettings;
using IOptions_AppSettings = Microsoft.Extensions.Options.IOptions<TILSOFTAI.Configuration.AppSettings>;

namespace TILSOFTAI.Orchestration.Modules.EntityGraph;

public sealed class EntityGraphToolRegistrationProvider : IToolRegistrationProvider
{
    private readonly AppSettings _settings;
    private readonly DynamicIntentValidator _validator;
    private readonly ToolInputSpecCatalog _specCatalog;

    public EntityGraphToolRegistrationProvider(
        IOptions_AppSettings settings,
        DynamicIntentValidator validator,
        ToolInputSpecCatalog specCatalog)
    {
        _settings = settings.Value;
        _validator = validator;
        _specCatalog = specCatalog;
    }

    public IEnumerable<ToolDefinition> GetToolDefinitions()
    {
        // Check global feature flag first
        if (!_settings.EntityGraph.Enabled)
        {
            yield break;
        }

        // atomic.graph.search
        if (_settings.Orchestration.ToolAllowlist.Contains("atomic.graph.search"))
        {
            yield return Dynamic("atomic.graph.search", requiresWrite: false);
        }

        // atomic.graph.get
        if (_settings.Orchestration.ToolAllowlist.Contains("atomic.graph.get"))
        {
            yield return Dynamic("atomic.graph.get", requiresWrite: false);
        }
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
