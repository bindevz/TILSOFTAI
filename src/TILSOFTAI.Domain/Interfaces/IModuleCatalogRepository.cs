using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

/// <summary>
/// SQL-backed module routing catalog.
///
/// Design goals:
/// - Remove hard-coded module routing rules from C#.
/// - Allow scaling to many modules by managing routing signals in SQL.
/// - Keep orchestration deterministic; signals are not inferred by the LLM.
/// </summary>
public interface IModuleCatalogRepository
{
    /// <summary>
    /// Returns enabled module routing signal rows.
    /// </summary>
    Task<IReadOnlyList<ModuleSignalRow>> GetEnabledAsync(CancellationToken cancellationToken);
}
