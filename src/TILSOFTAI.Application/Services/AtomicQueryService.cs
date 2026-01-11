using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Application.Services;

/// <summary>
/// Executes stored procedures following the standardized AtomicQuery template.
/// </summary>
public sealed class AtomicQueryService
{
    private readonly IAtomicQueryRepository _repo;

    public AtomicQueryService(IAtomicQueryRepository repo)
    {
        _repo = repo;
    }

    public Task<AtomicQueryResult> ExecuteAsync(
        string storedProcedure,
        IReadOnlyDictionary<string, object?> parameters,
        AtomicQueryReadOptions readOptions,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storedProcedure))
            throw new ArgumentException("storedProcedure is required.");

        return _repo.ExecuteAsync(storedProcedure, parameters, readOptions, cancellationToken);
    }
}
