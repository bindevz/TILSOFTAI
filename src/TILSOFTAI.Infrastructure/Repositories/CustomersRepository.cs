using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Data;

namespace TILSOFTAI.Infrastructure.Repositories;

public sealed class CustomersRepository : ICustomersRepository
{
    private readonly SqlServerDbContext _dbContext;

    public CustomersRepository(SqlServerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Customer?> GetByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.Customers
            .Where(c => c.TenantId == tenantId && c.Id == id)
            .AsTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Customer?> GetByEmailAsync(string tenantId, string email, CancellationToken cancellationToken)
    {
        return _dbContext.Customers
            .Where(c => c.TenantId == tenantId && c.Email == email)
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task UpdateAsync(Customer customer, CancellationToken cancellationToken)
    {
        _dbContext.Customers.Update(customer);
        return Task.CompletedTask;
    }

    public async Task<PagedResult<Customer>> SearchAsync(string tenantId, string query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var normalized = query.Trim();
        var scoped = _dbContext.Customers
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId &&
                        (EF.Functions.Like(c.Name, $"%{normalized}%") || EF.Functions.Like(c.Email, $"%{normalized}%")));

        var totalCount = await scoped.CountAsync(cancellationToken);

        var items = await scoped
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Customer>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        };
    }
}
