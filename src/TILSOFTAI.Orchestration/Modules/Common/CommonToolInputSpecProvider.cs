using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Modules.Common;

public sealed class CommonToolInputSpecProvider : IToolInputSpecProvider
{
    public IEnumerable<ToolInputSpec> GetSpecs()
    {
        yield return BuildFiltersCatalog();
        yield return BuildActionsCatalog();
    }

    private static ToolInputSpec BuildFiltersCatalog()
    {
        var spec = Default("filters.catalog");
        spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        spec.Args["resource"] = new ToolArgSpec("resource", ToolArgType.String, Required: false, Default: null);
        spec.Args["includeValues"] = new ToolArgSpec("includeValues", ToolArgType.Bool, Required: false, Default: false);
        return spec;
    }

    private static ToolInputSpec BuildActionsCatalog()
    {
        var spec = Default("actions.catalog");
        spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        spec.Args["action"] = new ToolArgSpec("action", ToolArgType.String, Required: false, Default: null);
        spec.Args["includeExamples"] = new ToolArgSpec("includeExamples", ToolArgType.Bool, Required: false, Default: false);
        return spec;
    }

    private static ToolInputSpec Default(string toolName) => new()
    {
        ToolName = toolName,
        SupportsPaging = false,
        DefaultPage = 1,
        DefaultPageSize = 20,
        MaxPageSize = 200,
        AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    };
}
