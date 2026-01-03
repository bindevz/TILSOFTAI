using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

public interface IModelRepository
{
    Task<PagedResult<Model>> SearchAsync(string tenantId, string? rangeName, string? modelCode, string? modelName, string? season, string? collection, int page, int size, CancellationToken cancellationToken);

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
    Task<Model?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ModelAttribute>> ListAttributesAsync(string tenantId, Guid modelId, CancellationToken cancellationToken);
    Task<PriceAnalysis> AnalyzePriceAsync(string tenantId, Guid modelId, CancellationToken cancellationToken);
    Task CreateAsync(Model model, CancellationToken cancellationToken);
}
