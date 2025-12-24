using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Data;

namespace TILSOFTAI.Infrastructure.Repositories;

public sealed class ModelRepository : IModelRepository
{
    private readonly SqlServerDbContext _dbContext;

    public ModelRepository(SqlServerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<ProductModel>> SearchAsync(string tenantId, string? category, string? name, int page, int size, CancellationToken cancellationToken)
    {
        var scoped = _dbContext.ProductModels
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(category))
        {
            scoped = scoped.Where(m => m.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(name))
        {
            scoped = scoped.Where(m => m.Name.Contains(name));
        }

        var total = await scoped.CountAsync(cancellationToken);
        var items = await scoped
            .OrderBy(m => m.Name)
            .Skip((page - 1) * size)
            .Take(size)
            .Include(m => m.Attributes)
            .ToListAsync(cancellationToken);

        return new PagedResult<ProductModel>
        {
            Items = items,
            TotalCount = total,
            PageNumber = page,
            PageSize = size
        };
    }

    public Task<ProductModel?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.ProductModels
            .AsNoTracking()
            .Include(m => m.Attributes)
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ProductModelAttribute>> ListAttributesAsync(string tenantId, Guid modelId, CancellationToken cancellationToken)
    {
        return await _dbContext.ProductModelAttributes
            .AsNoTracking()
            .Where(a => a.ModelId == modelId && a.Model.TenantId == tenantId)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<PriceAnalysis> AnalyzePriceAsync(string tenantId, Guid modelId, CancellationToken cancellationToken)
    {
        var model = await _dbContext.ProductModels
            .AsNoTracking()
            .Include(m => m.Attributes)
            .FirstOrDefaultAsync(m => m.Id == modelId && m.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");

        var adjustment = model.Attributes.Count * 5m;
        var final = model.BasePrice + adjustment;
        return new PriceAnalysis(model.BasePrice, adjustment, final);
    }

    public async Task CreateAsync(ProductModel model, CancellationToken cancellationToken)
    {
        await _dbContext.ProductModels.AddAsync(model, cancellationToken);
    }
}
