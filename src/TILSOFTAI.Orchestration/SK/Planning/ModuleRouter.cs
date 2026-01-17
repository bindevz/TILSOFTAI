using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.SK.Plugins;
using TILSOFTAI.Orchestration.Tools.FiltersCatalog;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Orchestration.SK.Planning;

/// <summary>
/// Selects which module plugins should be exposed to the LLM for a given user turn.
///
/// Ver30 (Phase 2): routing signals are loaded from SQL (dbo.TILSOFTAI_ModuleCatalog) to remove
/// hard-coded routing dicts and scale to many modules.
/// </summary>
public sealed class ModuleRouter
{
    private sealed record ModuleSignals(string Module, string[] Keys, int Priority);

    private static readonly char[] SignalSeparators = new[] { '|', ',', ';', '\n', '\r', '\t' };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IAppCache _cache;
    private readonly PluginCatalog _pluginCatalog;
    private readonly IFilterCatalogRegistry _filterCatalogRegistry;
    private readonly ILogger<ModuleRouter> _logger;

    public ModuleRouter(
        IServiceScopeFactory scopeFactory,
        IAppCache cache,
        PluginCatalog pluginCatalog,
        IFilterCatalogRegistry filterCatalogRegistry,
        ILogger<ModuleRouter>? logger = null)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _pluginCatalog = pluginCatalog;
        _filterCatalogRegistry = filterCatalogRegistry;
        _logger = logger ?? NullLogger<ModuleRouter>.Instance;
    }

    public async Task<IReadOnlyCollection<string>> SelectModulesAsync(
        string userText,
        TSExecutionContext context,
        CancellationToken cancellationToken)
    {
        // context is reserved for future use (RBAC/module visibility).
        _ = context;

        var t = (userText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(t))
            return Array.Empty<string>();

        var specs = await GetSignalsAsync(cancellationToken);
        if (specs.Count == 0)
            return Array.Empty<string>();

        var candidates = new List<(string module, int score)>(specs.Count);
        foreach (var s in specs.Values)
        {
            // Only select modules that actually exist in the plugin catalog (fail-closed).
            if (!_pluginCatalog.ByModule.ContainsKey(s.Module) && !string.Equals(s.Module, "common", StringComparison.OrdinalIgnoreCase))
                continue;

            var score = Score(t, s);
            if (score > 0)
                candidates.Add((s.Module, score));
        }

        if (candidates.Count == 0)
            return Array.Empty<string>();

        // pick top modules by score
        var chosen = candidates
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.module, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .Select(x => x.module)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Always expose common utilities when at least one business module is selected.
        if (chosen.Count > 0)
            chosen.Add("common");

        return chosen.ToArray();
    }

    private int Score(string text, ModuleSignals spec)
    {
        var score = 0;

        // Strong boost for explicit module mention.
        if (text.Contains(spec.Module, StringComparison.OrdinalIgnoreCase))
            score += 3;

        foreach (var k in spec.Keys)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;
            var kk = k.Trim();
            if (kk.Length < 3) continue;
            if (text.Contains(kk, StringComparison.OrdinalIgnoreCase))
                score += kk.Length >= 10 ? 2 : 1;
        }

        // SQL priority is an additive bias.
        score += Math.Clamp(spec.Priority, -5, 20);
        return score;
    }

    private async Task<Dictionary<string, ModuleSignals>> GetSignalsAsync(CancellationToken ct)
    {
        return await _cache.GetOrAddAsync(
            key: "router:moduleSignals:v1",
            factory: async () =>
            {
                var dict = new Dictionary<string, (HashSet<string> keys, int priority)>(StringComparer.OrdinalIgnoreCase);

                // 1) Load signals from SQL catalog
                using (var scope = _scopeFactory.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetService<IModuleCatalogRepository>();
                    if (repo is not null)
                    {
                        var rows = await repo.GetEnabledAsync(ct);
                        foreach (var r in rows)
                        {
                            var module = (r.ModuleName ?? string.Empty).Trim();
                            if (string.IsNullOrWhiteSpace(module)) continue;

                            if (!dict.TryGetValue(module, out var entry))
                                entry = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);

                            entry.priority = r.Priority;
                            entry.keys.Add(module); // always include the module token

                            foreach (var s in SplitSignals(r.Signals))
                                entry.keys.Add(s);

                            dict[module] = entry;
                        }
                    }
                }

                // 2) Augment with filter-catalog signals (module -> filter keys/aliases)
                foreach (var resource in _filterCatalogRegistry.ListResources())
                {
                    if (!_filterCatalogRegistry.TryGet(resource, out var cat)) continue;

                    var module = resource.Split('.', 2, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (string.IsNullOrWhiteSpace(module)) continue;

                    if (!dict.TryGetValue(module, out var entry))
                        entry = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);

                    entry.keys.Add(module);

                    foreach (var f in cat.SupportedFilters)
                    {
                        if (!string.IsNullOrWhiteSpace(f.Key)) entry.keys.Add(f.Key);
                        if (f.Aliases is { Length: > 0 })
                        {
                            foreach (var a in f.Aliases)
                                if (!string.IsNullOrWhiteSpace(a)) entry.keys.Add(a);
                        }
                    }

                    dict[module] = entry;
                }

                var final = dict
                    .Select(kvp => new ModuleSignals(kvp.Key, kvp.Value.keys.ToArray(), kvp.Value.priority))
                    .ToDictionary(x => x.Module, x => x, StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation("ModuleRouter signals loaded modules={Modules}", string.Join(",", final.Keys.OrderBy(x => x)));
                return final;
            },
            ttl: TimeSpan.FromMinutes(10));
    }

    private IEnumerable<string> SplitSignals(string? signals)
    {
        if (string.IsNullOrWhiteSpace(signals))
            yield break;

        foreach (var raw in signals.Split(SignalSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var s = raw.Trim();
            if (string.IsNullOrWhiteSpace(s)) continue;
            yield return s;
        }
    }
}
