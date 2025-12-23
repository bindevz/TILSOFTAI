namespace TILSOFTAI.Domain.Entities;

public sealed class Order
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public Guid CustomerId { get; init; }
    public DateTimeOffset OrderDate { get; init; }
    public OrderStatus Status { get; init; }
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = "USD";
    public string? Reference { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public enum OrderStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Cancelled = 3
}
