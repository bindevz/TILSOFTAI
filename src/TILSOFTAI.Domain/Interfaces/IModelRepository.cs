using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

public interface IModelRepository
{
    /// <summary>
    /// Returns raw tabular rows for analytics. This avoids mapping to domain entities/DTOs.
    /// </summary>
    Task<TabularData> SearchTabularAsync(
        string tenantId,
        string? rangeName,
        string? modelCode,
        string? modelName,
        string? season,
        string? collection,
        int page,
        int size,
        CancellationToken cancellationToken);

    // Phase 2 (enterprise): pre-aggregated statistics for professional, guided answers.
    Task<ModelsStatsResult> GetStatsAsync(
        string tenantId,
        string? rangeName,
        string? modelCode,
        string? modelName,
        string? season,
        string? collection,
        int topN,
        CancellationToken cancellationToken);

    // Phase 2 (enterprise): option catalog for a single model (for validation + guided creation flows).
    Task<ModelsOptionsResult> GetOptionsAsync(
        string tenantId,
        int modelId,
        bool includeConstraints,
        CancellationToken cancellationToken);
    Task<PriceAnalysis> AnalyzePriceAsync(string tenantId, Guid modelId, CancellationToken cancellationToken);
}
