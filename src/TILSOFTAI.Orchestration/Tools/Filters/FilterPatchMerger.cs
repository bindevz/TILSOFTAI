using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Tools.Filters;

/// <summary>
/// Default implementation of <see cref="IFilterPatchMerger"/>.
/// - Canonicalizes both base and patch using filters-catalog (alias mapping).
/// - Applies patch values as override; missing keys are retained from base.
/// - A null/empty patch value removes the key.
/// </summary>
public sealed class FilterPatchMerger : IFilterPatchMerger
{
    private readonly IFilterCanonicalizer _canonicalizer;

    public FilterPatchMerger(IFilterCanonicalizer canonicalizer)
    {
        _canonicalizer = canonicalizer;
    }

    public FilterPatchMergeResult Merge(
        string resource,
        IReadOnlyDictionary<string, string?> baseFilters,
        IReadOnlyDictionary<string, string?> patchFilters)
    {
        // Canonicalize base (defensive) and patch (required)
        var (baseApplied, _) = _canonicalizer.Canonicalize(resource, baseFilters);

        // If patch is empty, treat as "no changes" => retain base.
        if (patchFilters is null || patchFilters.Count == 0)
        {
            return new FilterPatchMergeResult(
                new Dictionary<string, string?>(baseApplied, StringComparer.OrdinalIgnoreCase),
                Array.Empty<string>());
        }

        var (patchApplied, rejected) = _canonicalizer.Canonicalize(resource, patchFilters);

        var merged = new Dictionary<string, string?>(baseApplied, StringComparer.OrdinalIgnoreCase);

        foreach (var (k, v) in patchApplied)
        {
            if (string.IsNullOrWhiteSpace(k))
                continue;

            var val = v?.Trim();
            if (string.IsNullOrWhiteSpace(val))
            {
                // Null/empty means remove filter.
                merged.Remove(k);
            }
            else
            {
                merged[k] = val;
            }
        }

        return new FilterPatchMergeResult(merged, rejected);
    }
}
