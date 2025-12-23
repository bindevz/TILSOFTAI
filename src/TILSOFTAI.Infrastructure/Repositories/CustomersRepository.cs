using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;
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
}
