using TILSOFTAI.Orchestration.Tools.FiltersCatalog;

namespace TILSOFTAI.Orchestration.Tools.Modularity;

public interface IFilterCanonicalizer
{
    (Dictionary<string, string?> Applied, string[] Rejected) Canonicalize(string resource, IReadOnlyDictionary<string, string?> incoming);
}

public sealed class FilterCanonicalizer : IFilterCanonicalizer
{
    private readonly IFilterCatalogRegistry _registry;

    public FilterCanonicalizer(IFilterCatalogRegistry registry) => _registry = registry;

    public (Dictionary<string, string?> Applied, string[] Rejected) Canonicalize(string resource, IReadOnlyDictionary<string, string?> incoming)
    {
        if (incoming.Count == 0)
            return (new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), Array.Empty<string>());

        // If catalog missing, fail closed by rejecting all keys.
        if (!_registry.TryGet(resource, out var catalog))
            return (new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), incoming.Keys.ToArray());

        // Build alias -> canonical mapping
        var aliasToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in catalog.SupportedFilters)
        {
            aliasToKey[f.Key] = f.Key;
            if (f.Aliases is null) continue;
            foreach (var a in f.Aliases)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                aliasToKey[a.Trim()] = f.Key;
            }
        }

        var applied = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var rejected = new List<string>();

        foreach (var (k, v) in incoming)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;

            var key = k.Trim();
            if (!aliasToKey.TryGetValue(key, out var canonicalKey))
            {
                rejected.Add(k);
                continue;
            }

            applied[canonicalKey] = v?.Trim();
        }

        return (applied, rejected.ToArray());
    }
}
