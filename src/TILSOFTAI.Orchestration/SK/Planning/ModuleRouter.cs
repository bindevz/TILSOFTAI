using TILSOFTAI.Orchestration.Tools.FiltersCatalog;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Orchestration.SK.Planning;

public sealed class ModuleRouter
{
    private readonly IReadOnlyDictionary<string, string[]> _moduleSignals;

    public ModuleRouter(IFilterCatalogRegistry filterCatalogRegistry)
    {
        var dict = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            // AtomicQuery + AtomicDataEngine live under the "analytics" module.
            // We keep business keywords here to ensure the analytics tools are exposed
            // even when the user talks about "model" or other ERP entities.
            ["analytics"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "model", "mẫu", "mô hình", "sản phẩm", "sku", "attribute", "giá"
            }
        };

        foreach (var resource in filterCatalogRegistry.ListResources())
        {
            if (!filterCatalogRegistry.TryGet(resource, out var cat)) continue;

            var module = resource.Split('.', 2, StringSplitOptions.RemoveEmptyEntries)[0];
            if (string.IsNullOrWhiteSpace(module)) continue;

            if (!dict.TryGetValue(module, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                dict[module] = set;
            }

            set.Add(module);

            foreach (var f in cat.SupportedFilters)
            {
                if (!string.IsNullOrWhiteSpace(f.Key)) set.Add(f.Key);
                if (f.Aliases is { Length: > 0 })
                {
                    foreach (var a in f.Aliases)
                    {
                        if (!string.IsNullOrWhiteSpace(a)) set.Add(a);
                    }
                }
            }
        }

        _moduleSignals = dict.ToDictionary(k => k.Key, v => v.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> SelectModules(string userText, TSExecutionContext context)
    {
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var t = userText ?? string.Empty;

        foreach (var kvp in _moduleSignals)
        {
            if (ContainsAny(t, kvp.Value))
                modules.Add(kvp.Key);
        }

        // Analytics/reporting often needs the analytics module
        if (ContainsAny(t, "báo cáo", "phân tích", "doanh số", "lợi nhuận", "kpi", "trend", "xu hướng"))
            modules.Add("analytics");

        if (modules.Count == 0)
            return Array.Empty<string>();

        modules.Add("common");
        return modules;
    }

    private static bool ContainsAny(string text, params string[] keys)
        => keys.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
