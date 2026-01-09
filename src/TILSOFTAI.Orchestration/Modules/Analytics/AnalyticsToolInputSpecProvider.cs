using TILSOFTAI.Orchestration.Tools.FiltersCatalog;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Modules.Analytics;

public sealed class AnalyticsToolInputSpecProvider : IToolInputSpecProvider
{
    private readonly IFilterCatalogRegistry _filterRegistry;

    public AnalyticsToolInputSpecProvider(IFilterCatalogRegistry filterRegistry)
    {
        _filterRegistry = filterRegistry;
    }

    public IEnumerable<ToolInputSpec> GetSpecs()
    {
        yield return BuildDatasetCreate();
        yield return BuildRun();
    }

    private ToolInputSpec BuildDatasetCreate()
    {
        var spec = Default("analytics.dataset.create");
        spec.SupportsPaging = false;
        spec.AllowedFilterKeys = FilterKeySetBuilder.Build(_filterRegistry, "analytics.dataset.create");

        spec.Args["source"] = new ToolArgSpec("source", ToolArgType.String, Required: false, Default: "models");
        spec.Args["select"] = new ToolArgSpec("select", ToolArgType.Json, Required: false, Default: null);
        spec.Args["maxRows"] = new ToolArgSpec("maxRows", ToolArgType.Int, Required: false, Default: 20000, MinInt: 1, MaxInt: 100000);
        spec.Args["maxColumns"] = new ToolArgSpec("maxColumns", ToolArgType.Int, Required: false, Default: 40, MinInt: 1, MaxInt: 100);
        spec.Args["previewRows"] = new ToolArgSpec("previewRows", ToolArgType.Int, Required: false, Default: 100, MinInt: 0, MaxInt: 200);
        return spec;
    }

    private ToolInputSpec BuildRun()
    {
        var spec = Default("analytics.run");
        spec.SupportsPaging = false;
        spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        spec.Args["datasetId"] = new ToolArgSpec("datasetId", ToolArgType.String, Required: true);
        spec.Args["pipeline"] = new ToolArgSpec("pipeline", ToolArgType.Json, Required: true);
        spec.Args["topN"] = new ToolArgSpec("topN", ToolArgType.Int, Required: false, Default: 20, MinInt: 1, MaxInt: 200);
        spec.Args["maxGroups"] = new ToolArgSpec("maxGroups", ToolArgType.Int, Required: false, Default: 200, MinInt: 1, MaxInt: 5000);
        spec.Args["maxResultRows"] = new ToolArgSpec("maxResultRows", ToolArgType.Int, Required: false, Default: 500, MinInt: 1, MaxInt: 5000);
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
