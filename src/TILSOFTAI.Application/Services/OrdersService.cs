using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Application.Validators;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Caching;

namespace TILSOFTAI.Application.Services;

public sealed class OrdersService
{
    private readonly IOrdersRepository _ordersRepository;
    private readonly RbacService _rbac;
    private readonly AppMemoryCache _cache;

    public OrdersService(IOrdersRepository ordersRepository, RbacService rbac, AppMemoryCache cache)
    {
        _ordersRepository = ordersRepository;
        _rbac = rbac;
        _cache = cache;
    }

    public async Task<PagedResult<Order>> QueryOrdersAsync(OrderQuery query, TILSOFTAI.Domain.ValueObjects.ExecutionContext context, CancellationToken cancellationToken)
    {
        _rbac.EnsureReadAllowed("orders.query", context);
        BusinessValidators.ValidateOrderQuery(query);

        return await _ordersRepository.QueryAsync(context.TenantId, query, cancellationToken);
    }

    public async Task<OrderSummary> SummarizeOrdersAsync(OrderQuery query, TILSOFTAI.Domain.ValueObjects.ExecutionContext context, CancellationToken cancellationToken)
    {
        _rbac.EnsureReadAllowed("orders.summary", context);
        BusinessValidators.ValidateOrderQuery(query);

        var cacheKey = $"orders:summary:{context.TenantId}:{query.CustomerId}:{query.Status}:{query.StartDate}:{query.EndDate}";
        return await _cache.GetOrAddAsync(cacheKey,
            () => _ordersRepository.SummarizeAsync(context.TenantId, query, cancellationToken),
            TimeSpan.FromMinutes(1));
    }
}
