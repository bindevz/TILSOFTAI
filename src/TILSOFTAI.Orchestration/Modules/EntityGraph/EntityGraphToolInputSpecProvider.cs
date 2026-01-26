using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Modules.EntityGraph;

public sealed class EntityGraphToolInputSpecProvider : IToolInputSpecProvider
{
    public IEnumerable<ToolInputSpec> GetSpecs()
    {
        yield return BuildSearch();
        yield return BuildGet();
    }

    private static ToolInputSpec BuildSearch()
    {
        var spec = Default("atomic.graph.search");
        spec.Args["query"] = new ToolArgSpec("query", ToolArgType.String, Required: true);
        spec.Args["topK"] = new ToolArgSpec("topK", ToolArgType.Int, Default: 5, MinInt: 1, MaxInt: 20);
        return spec;
    }

    private static ToolInputSpec BuildGet()
    {
        var spec = Default("atomic.graph.get");
        spec.Args["graphCode"] = new ToolArgSpec("graphCode", ToolArgType.String, Required: true);
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
