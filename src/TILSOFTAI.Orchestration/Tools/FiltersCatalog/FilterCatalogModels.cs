namespace TILSOFTAI.Orchestration.Tools.FiltersCatalog;

/// <summary>
/// Describes a single supported filter key for a given resource/tool family.
/// This is used by filters.catalog and for validating tool inputs.
/// </summary>
public sealed record FilterDescriptor(
    string Key,
    string Type,
    string Description,
    string[] Examples,
    string[]? Operators = null,
    string[]? Aliases = null,
    string? Normalize = null);

/// <summary>
/// Catalog of supported filters for a resource (e.g. models.search).
/// </summary>
public sealed record ResourceFilterCatalog(
    string Resource,
    string Title,
    string Description,
    IReadOnlyList<FilterDescriptor> SupportedFilters,
    object Usage);
