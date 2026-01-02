namespace TILSOFTAI.Orchestration.Tools.FiltersCatalog;

public sealed record FilterDescriptor(
    string Key,
    string Type,
    string Description,
    string[] Examples,
    string[]? Operators = null,
    string[]? Aliases = null,
    string? Normalize = null);

public sealed record ResourceFilterCatalog(
    string Resource,
    string Title,
    string Description,
    IReadOnlyList<FilterDescriptor> SupportedFilters,
    object Usage // giữ usage dạng object để linh hoạt
);
