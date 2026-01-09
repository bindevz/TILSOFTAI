using TILSOFTAI.Orchestration.Tools.FiltersCatalog;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Modules.Models;

public sealed class ModelsToolInputSpecProvider : IToolInputSpecProvider
{
    private readonly IFilterCatalogRegistry _filterRegistry;

    public ModelsToolInputSpecProvider(IFilterCatalogRegistry filterRegistry)
    {
        _filterRegistry = filterRegistry;
    }

    public IEnumerable<ToolInputSpec> GetSpecs()
    {
        yield return BuildModelsSearch();
        yield return BuildModelsCount();
        yield return BuildModelsStats();
        yield return BuildModelsOptions();
        yield return BuildModelsGet();
        yield return BuildModelsGuidOnly("models.attributes.list");
        yield return BuildModelsGuidOnly("models.price.analyze");
        yield return BuildModelsCreatePrepare();
        yield return BuildModelsCreateCommit();
    }

    private ToolInputSpec BuildModelsSearch()
    {
        var spec = Default("models.search");
        spec.SupportsPaging = true;
        spec.DefaultPageSize = 20;
        spec.MaxPageSize = 200;
        spec.AllowedFilterKeys = FilterKeySetBuilder.Build(_filterRegistry, "models.search");
        return spec;
    }

    private ToolInputSpec BuildModelsCount()
    {
        var spec = Default("models.count");
        spec.SupportsPaging = false;
        spec.AllowedFilterKeys = FilterKeySetBuilder.Build(_filterRegistry, "models.count");
        return spec;
    }

    private ToolInputSpec BuildModelsStats()
    {
        var spec = Default("models.stats");
        spec.SupportsPaging = false;
        spec.AllowedFilterKeys = FilterKeySetBuilder.Build(_filterRegistry, "models.stats");
        spec.Args["topN"] = new ToolArgSpec("topN", ToolArgType.Int, Required: false, Default: 10, MinInt: 1, MaxInt: 50);
        return spec;
    }

    private ToolInputSpec BuildModelsOptions()
    {
        var spec = Default("models.options");
        spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        spec.Args["modelId"] = new ToolArgSpec("modelId", ToolArgType.Int, Required: true, MinInt: 1, MaxInt: int.MaxValue);
        spec.Args["includeConstraints"] = new ToolArgSpec("includeConstraints", ToolArgType.Bool, Required: false, Default: true);
        return spec;
    }

    
    private ToolInputSpec BuildModelsGet()
    {
        var spec = Default("models.get");
        spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        spec.Args["modelId"] = new ToolArgSpec("modelId", ToolArgType.Int, Required: true, MinInt: 1, MaxInt: int.MaxValue);
        return spec;
    }

    private ToolInputSpec BuildModelsGuidOnly(string toolName)
    {
        var spec = Default(toolName);
        spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        spec.Args["modelId"] = new ToolArgSpec("modelId", ToolArgType.Guid, Required: true);
        return spec;
    }

    private ToolInputSpec BuildModelsCreatePrepare()
    {
        var spec = Default("models.create.prepare");
        spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        spec.Args["name"] = new ToolArgSpec("name", ToolArgType.String, Required: true);
        spec.Args["category"] = new ToolArgSpec("category", ToolArgType.String, Required: true);
        spec.Args["basePrice"] = new ToolArgSpec("basePrice", ToolArgType.Decimal, Required: true);
        spec.Args["attributes"] = new ToolArgSpec("attributes", ToolArgType.StringMap, Required: false,
            Default: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        return spec;
    }

    private ToolInputSpec BuildModelsCreateCommit()
    {
        var spec = Default("models.create.commit");
        spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        spec.Args["confirmationId"] = new ToolArgSpec("confirmationId", ToolArgType.String, Required: true);
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
