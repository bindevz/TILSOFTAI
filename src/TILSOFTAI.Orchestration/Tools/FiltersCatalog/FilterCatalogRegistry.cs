using System.Collections.ObjectModel;

namespace TILSOFTAI.Orchestration.Tools.FiltersCatalog;

public interface IFilterCatalogRegistry
{
    IReadOnlyCollection<string> ListResources();
    bool TryGet(string resource, out ResourceFilterCatalog catalog);
}

/// <summary>
/// Module extension point. Each module can contribute its own filter catalogs.
/// </summary>
public interface IFilterCatalogProvider
{
    IEnumerable<ResourceFilterCatalog> GetCatalogs();
}

/// <summary>
/// Aggregates filter catalogs from all loaded modules.
/// </summary>
public sealed class FilterCatalogRegistry : IFilterCatalogRegistry
{
    private readonly IReadOnlyDictionary<string, ResourceFilterCatalog> _catalogs;

    public FilterCatalogRegistry(IEnumerable<IFilterCatalogProvider> providers)
    {
        var dict = new Dictionary<string, ResourceFilterCatalog>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in providers)
        {
            foreach (var c in p.GetCatalogs())
            {
                if (string.IsNullOrWhiteSpace(c.Resource))
                    throw new InvalidOperationException("Filter catalog has empty resource.");

                if (!dict.TryAdd(c.Resource, c))
                    throw new InvalidOperationException($"Duplicate filters.catalog resource registered: {c.Resource}");
            }
        }

        _catalogs = new ReadOnlyDictionary<string, ResourceFilterCatalog>(dict);
    }

    public IReadOnlyCollection<string> ListResources() => _catalogs.Keys.ToArray();

    public bool TryGet(string resource, out ResourceFilterCatalog catalog)
        => _catalogs.TryGetValue(resource, out catalog!);
}
