using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

public interface ICustomersRepository
{
    Task<Customer?> GetByIdAsync(string tenantId, Guid id, CancellationToken cancellationToken);
    Task<Customer?> GetByEmailAsync(string tenantId, string email, CancellationToken cancellationToken);
    Task UpdateAsync(Customer customer, CancellationToken cancellationToken);
    Task<PagedResult<Customer>> SearchAsync(string tenantId, string query, int page, int pageSize, CancellationToken cancellationToken);
}
