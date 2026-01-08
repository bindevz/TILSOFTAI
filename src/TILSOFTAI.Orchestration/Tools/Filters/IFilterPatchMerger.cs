namespace TILSOFTAI.Orchestration.Tools.Filters;

public sealed record FilterPatchMergeResult(
    Dictionary<string, string?> Merged,
    string[] RejectedKeys);

/// <summary>
/// Merges a patch of filters onto a base filter set using filters-catalog.
/// The merger MUST NOT hard-code filter keys; only keys/aliases present in the catalog are accepted.
/// </summary>
public interface IFilterPatchMerger
{
    FilterPatchMergeResult Merge(
        string resource,
        IReadOnlyDictionary<string, string?> baseFilters,
        IReadOnlyDictionary<string, string?> patchFilters);
}
