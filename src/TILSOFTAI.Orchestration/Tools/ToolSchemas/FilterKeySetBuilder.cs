using TILSOFTAI.Orchestration.Tools.FiltersCatalog;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

internal static class FilterKeySetBuilder
{
    public static HashSet<string> Build(IFilterCatalogRegistry registry, string resource)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!registry.TryGet(resource, out var catalog))
            return allowed;

        foreach (var f in catalog.SupportedFilters)
        {
            allowed.Add(f.Key);
            if (f.Aliases is null) continue;
            foreach (var a in f.Aliases)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                allowed.Add(a);
            }
        }

        return allowed;
    }
}
