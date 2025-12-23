using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

public interface IOrdersRepository
{
    Task<PagedResult<Order>> QueryAsync(string tenantId, OrderQuery query, CancellationToken cancellationToken);
    Task<OrderSummary> SummarizeAsync(string tenantId, OrderQuery query, CancellationToken cancellationToken);
}
