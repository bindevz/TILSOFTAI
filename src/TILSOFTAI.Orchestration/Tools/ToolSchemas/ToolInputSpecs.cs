using TILSOFTAI.Orchestration.Tools.FiltersCatalog;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

internal static class ToolInputSpecs
{
    public static ToolInputSpec For(string toolName)
    {
        // Allowed filters come from the shared filters.catalog registry.
        var allowedFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (FilterCatalogRegistry.TryGet(toolName, out var catalog))
        {
            foreach (var f in catalog.SupportedFilters)
            {
                allowedFilters.Add(f.Key);
                if (f.Aliases is not null)
                {
                    foreach (var a in f.Aliases)
                    {
                        if (!string.IsNullOrWhiteSpace(a))
                            allowedFilters.Add(a);
                    }
                }
            }
        }

        toolName = toolName.ToLowerInvariant();

        // Defaults
        var spec = new ToolInputSpec
        {
            ToolName = toolName,
            SupportsPaging = false,
            DefaultPage = 1,
            DefaultPageSize = 20,
            MaxPageSize = 200,
            AllowedFilterKeys = allowedFilters
        };

        switch (toolName)
        {
            // Models (read)
            case "models.search":
                spec.SupportsPaging = true;
                spec.DefaultPageSize = 20;
                spec.MaxPageSize = 200;
                break;

            case "models.count":
                // filters only
                break;

            case "models.stats":
                spec.Args["topN"] = new ToolArgSpec("topN", ToolArgType.Int, Required: false, Default: 10, MinInt: 1, MaxInt: 50);
                break;

            case "models.options":
                spec.Args["modelId"] = new ToolArgSpec("modelId", ToolArgType.Int, Required: true, MinInt: 1, MaxInt: int.MaxValue);
                spec.Args["includeConstraints"] = new ToolArgSpec("includeConstraints", ToolArgType.Bool, Required: false, Default: true);
                // options does not accept filters
                spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                break;

            case "models.get":
            case "models.attributes.list":
            case "models.price.analyze":
                // Note: these tools use GUID modelId in the current contract.
                spec.Args["modelId"] = new ToolArgSpec("modelId", ToolArgType.Guid, Required: true);
                spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                break;

            // Models (write)
            case "models.create.prepare":
                spec.Args["name"] = new ToolArgSpec("name", ToolArgType.String, Required: true);
                spec.Args["category"] = new ToolArgSpec("category", ToolArgType.String, Required: true);
                spec.Args["basePrice"] = new ToolArgSpec("basePrice", ToolArgType.Decimal, Required: true);
                spec.Args["attributes"] = new ToolArgSpec("attributes", ToolArgType.StringMap, Required: false,
                    Default: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                break;

            case "models.create.commit":
                spec.Args["confirmationId"] = new ToolArgSpec("confirmationId", ToolArgType.String, Required: true);
                spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                break;

            // System
            case "filters.catalog":
                spec.Args["resource"] = new ToolArgSpec("resource", ToolArgType.String, Required: false, Default: null);
                spec.Args["includeValues"] = new ToolArgSpec("includeValues", ToolArgType.Bool, Required: false, Default: false);
                spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                break;

            case "actions.catalog":
                spec.Args["action"] = new ToolArgSpec("action", ToolArgType.String, Required: false, Default: null);
                spec.Args["includeExamples"] = new ToolArgSpec("includeExamples", ToolArgType.Bool, Required: false, Default: false);
                spec.AllowedFilterKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                break;
        }

        return spec;
    }
}
