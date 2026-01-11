using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

/// <summary>
/// Read-only access to the governed stored procedure catalog.
/// The catalog defines which stored procedures are AI-allowed and how to describe them.
/// </summary>
public interface IAtomicCatalogRepository
{
    /// <summary>
    /// Full-text search over the catalog.
    /// </summary>
    Task<IReadOnlyList<AtomicCatalogSearchHit>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns a catalog entry for the given stored procedure, or null if not found.
    /// The <paramref name="storedProcedure"/> is expected to be normalized (e.g., dbo.TILSOFTAI_sp_*).
    /// </summary>
    Task<AtomicCatalogEntry?> GetByNameAsync(
        string storedProcedure,
        CancellationToken cancellationToken);
}
