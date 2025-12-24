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
        var scoped = ApplyFilters(_dbContext.Orders.AsNoTracking(), tenantId, query);

        var totalCount = await scoped.CountAsync(cancellationToken);
        if (totalCount == 0)
        {
            return new OrderSummary
            {
                Count = 0,
                TotalAmount = 0,
                AverageAmount = 0,
                MinAmount = 0,
                MaxAmount = 0,
                CountByStatus = new Dictionary<OrderStatus, int>(),
                TopCustomers = Array.Empty<TopCustomerSpend>()
            };
        }

        var aggregates = await scoped.GroupBy(o => 1)
            .Select(g => new
            {
                TotalAmount = g.Sum(x => x.TotalAmount),
                MinAmount = g.Min(x => x.TotalAmount),
                MaxAmount = g.Max(x => x.TotalAmount)
            })
            .FirstAsync(cancellationToken);

        var countByStatus = await scoped
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(k => k.Status, v => v.Count, cancellationToken);

        var topCustomers = await scoped
            .GroupBy(o => o.CustomerId)
            .Select(g => new
            {
                CustomerId = g.Key,
                TotalAmount = g.Sum(x => x.TotalAmount),
                OrderCount = g.Count()
            })
            .OrderByDescending(x => x.TotalAmount)
            .Take(5)
            .Join(_dbContext.Customers.AsNoTracking().Where(c => c.TenantId == tenantId),
                g => g.CustomerId,
                c => c.Id,
                (g, c) => new TopCustomerSpend(g.CustomerId, c.Name, g.TotalAmount, g.OrderCount))
            .ToListAsync(cancellationToken);

        var average = aggregates.TotalAmount / totalCount;

        return new OrderSummary
        {
            Count = totalCount,
            TotalAmount = aggregates.TotalAmount,
            AverageAmount = average,
            MinAmount = aggregates.MinAmount,
            MaxAmount = aggregates.MaxAmount,
            CountByStatus = countByStatus,
            TopCustomers = topCustomers
        };
    }

    public Task CreateAsync(Order order, CancellationToken cancellationToken)
    {
        _dbContext.Orders.Add(order);
        return Task.CompletedTask;
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
