using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Modules.Analytics;

public sealed class AnalyticsToolInputSpecProvider : IToolInputSpecProvider
{
    public IEnumerable<ToolInputSpec> GetSpecs()
    {
        yield return BuildRun();
        yield return BuildAtomicQueryExecute();
        yield return BuildAtomicCatalogSearch();
    }

    private static ToolInputSpec BuildRun()
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

    private static ToolInputSpec BuildAtomicQueryExecute()
    {
        var spec = Default("atomic.query.execute");
        spec.SupportsPaging = false;
        spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        spec.Args["spName"] = new ToolArgSpec("spName", ToolArgType.String, Required: true);
        spec.Args["params"] = new ToolArgSpec("params", ToolArgType.Json, Required: false, Default: null);

        spec.Args["maxRowsPerTable"] = new ToolArgSpec("maxRowsPerTable", ToolArgType.Int, Required: false, Default: 20000, MinInt: 1, MaxInt: 200000);
        spec.Args["maxRowsSummary"] = new ToolArgSpec("maxRowsSummary", ToolArgType.Int, Required: false, Default: 500, MinInt: 0, MaxInt: 50000);
        spec.Args["maxSchemaRows"] = new ToolArgSpec("maxSchemaRows", ToolArgType.Int, Required: false, Default: 50000, MinInt: 1, MaxInt: 500000);
        spec.Args["maxTables"] = new ToolArgSpec("maxTables", ToolArgType.Int, Required: false, Default: 20, MinInt: 1, MaxInt: 100);

        spec.Args["maxColumns"] = new ToolArgSpec("maxColumns", ToolArgType.Int, Required: false, Default: 100, MinInt: 1, MaxInt: 500);
        spec.Args["maxDisplayRows"] = new ToolArgSpec("maxDisplayRows", ToolArgType.Int, Required: false, Default: 2000, MinInt: 1, MaxInt: 20000);
        spec.Args["previewRows"] = new ToolArgSpec("previewRows", ToolArgType.Int, Required: false, Default: 100, MinInt: 0, MaxInt: 200);
        return spec;
    }

    private static ToolInputSpec BuildAtomicCatalogSearch()
    {
        var spec = Default("atomic.catalog.search");
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
