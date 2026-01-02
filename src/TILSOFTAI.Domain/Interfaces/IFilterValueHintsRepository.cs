using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

/// <summary>
/// Retrieves value-hints (top values, counts, etc.) for filters.catalog includeValues=true.
/// Implementations should be read-only and safe for LLM consumption.
/// </summary>
public interface IFilterValueHintsRepository
{
    Task<IReadOnlyList<FilterValueHintRow>> GetValueHintsAsync(
        string tenantId,
        string resource,
        int top,
        CancellationToken cancellationToken);
}
