using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

public interface IModelRepository
{
    Task<PagedResult<ProductModel>> SearchAsync(string tenantId, string? category, string? name, int page, int size, CancellationToken cancellationToken);
    Task<ProductModel?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ProductModelAttribute>> ListAttributesAsync(string tenantId, Guid modelId, CancellationToken cancellationToken);
    Task<PriceAnalysis> AnalyzePriceAsync(string tenantId, Guid modelId, CancellationToken cancellationToken);
    Task CreateAsync(ProductModel model, CancellationToken cancellationToken);
}
