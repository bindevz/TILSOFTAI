using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Modules.DocumentSearch;

public sealed class DocumentSearchToolInputSpecProvider : IToolInputSpecProvider
{
    public IEnumerable<ToolInputSpec> GetSpecs()
    {
        yield return BuildDocSearch();
    }

    private static ToolInputSpec BuildDocSearch()
    {
        var spec = Default("atomic.doc.search");
        spec.SupportsPaging = false;
        spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        spec.Args["query"] = new ToolArgSpec("query", ToolArgType.String, Required: true);
        spec.Args["topK"] = new ToolArgSpec("topK", ToolArgType.Int, Required: false, Default: 5, MinInt: 1, MaxInt: 20);
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
