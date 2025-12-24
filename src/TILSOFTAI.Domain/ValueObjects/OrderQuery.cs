using TILSOFTAI.Domain.Entities;

namespace TILSOFTAI.Domain.ValueObjects;

public sealed class OrderQuery
{
    public Guid? CustomerId { get; init; }
    public OrderStatus? Status { get; init; }
    public DateTimeOffset? StartDate { get; init; }
    public DateTimeOffset? EndDate { get; init; }
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 50;
}

public sealed class OrderSummary
{
    public required int Count { get; init; }
    public required decimal TotalAmount { get; init; }
    public required decimal AverageAmount { get; init; }
    public required decimal MinAmount { get; init; }
    public required decimal MaxAmount { get; init; }
    public required IReadOnlyDictionary<OrderStatus, int> CountByStatus { get; init; }
    public required IReadOnlyCollection<TopCustomerSpend> TopCustomers { get; init; }
}

public sealed record TopCustomerSpend(Guid CustomerId, string? CustomerName, decimal TotalAmount, int OrderCount);
