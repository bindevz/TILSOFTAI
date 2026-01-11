using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

/// <summary>
/// Executes SQL Server stored procedures that follow the standardized AtomicQuery template:
/// RS0 schema, RS1 summary (optional), RS2..N data tables.
/// </summary>
public interface IAtomicQueryRepository
{
    Task<AtomicQueryResult> ExecuteAsync(
        string storedProcedure,
        IReadOnlyDictionary<string, object?> parameters,
        AtomicQueryReadOptions readOptions,
        CancellationToken cancellationToken);
}
