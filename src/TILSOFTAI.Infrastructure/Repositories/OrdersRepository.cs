using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Data;

namespace TILSOFTAI.Infrastructure.Repositories;

public sealed class OrdersRepository : IOrdersRepository
{
    private readonly SqlServerDbContext _dbContext;

    public OrdersRepository(SqlServerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedResult<Order>> QueryAsync(string tenantId, OrderQuery query, CancellationToken cancellationToken)
    {
        var scoped = ApplyFilters(_dbContext.Orders.AsNoTracking(), tenantId, query);
        var totalCount = await scoped.CountAsync(cancellationToken);

        var items = await scoped
            .OrderByDescending(o => o.OrderDate)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Order>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        };
    }

    public async Task<OrderSummary> SummarizeAsync(string tenantId, OrderQuery query, CancellationToken cancellationToken)
    {
        var scoped = ApplyFilters(_dbContext.Orders.AsNoTracking(), tenantId, query)
            .OrderBy(o => o.Id);

        const int batchSize = 500;
        var offset = 0;
        var totalAmount = 0m;
        var totalCount = 0;
        var countByStatus = new Dictionary<OrderStatus, int>();

        while (true)
        {
            var batch = await scoped
                .Skip(offset)
                .Take(batchSize)
                .Select(o => new { o.TotalAmount, o.Status })
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            foreach (var item in batch)
            {
                totalCount++;
                totalAmount += item.TotalAmount;
                countByStatus[item.Status] = countByStatus.TryGetValue(item.Status, out var count)
                    ? count + 1
                    : 1;
            }

            if (batch.Count < batchSize)
            {
                break;
            }

            offset += batchSize;
        }

        var average = totalCount == 0 ? 0 : totalAmount / totalCount;

        return new OrderSummary
        {
            Count = totalCount,
            TotalAmount = totalAmount,
            AverageAmount = average,
            CountByStatus = countByStatus
        };
    }

    private static IQueryable<Order> ApplyFilters(IQueryable<Order> queryable, string tenantId, OrderQuery query)
    {
        var scoped = queryable.Where(o => o.TenantId == tenantId);

        if (query.CustomerId.HasValue)
        {
            scoped = scoped.Where(o => o.CustomerId == query.CustomerId);
        }

        if (query.Status.HasValue)
        {
            scoped = scoped.Where(o => o.Status == query.Status);
        }

        if (query.StartDate.HasValue)
        {
            scoped = scoped.Where(o => o.OrderDate >= query.StartDate.Value);
        }

        if (query.EndDate.HasValue)
        {
            scoped = scoped.Where(o => o.OrderDate <= query.EndDate.Value);
        }

        return scoped;
    }
}
