using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

/// <summary>
/// Optional SQL-backed configuration for table-kind guessing.
///
/// This is ONLY used as a fallback when RS0 schema is missing/unreadable.
/// By moving these rules to SQL, the system can scale to many modules without
/// hard-coded summarySignals in C#.
/// </summary>
public interface ITableKindSignalsRepository
{
    /// <summary>
    /// Returns enabled signals used by fallback table-kind classification.
    /// </summary>
    Task<IReadOnlyList<TableKindSignalRow>> GetEnabledAsync(CancellationToken cancellationToken);
}
