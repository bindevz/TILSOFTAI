namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// Defensive bounds when materializing AtomicQuery result sets.
/// These bounds are enforced in C# even if SQL forgets to apply TOP/paging.
/// </summary>
public sealed record AtomicQueryReadOptions(
    int MaxRowsPerTable = 20_000,
    int MaxRowsSummary = 500,
    int MaxSchemaRows = 50_000,
    int MaxTables = 20);
