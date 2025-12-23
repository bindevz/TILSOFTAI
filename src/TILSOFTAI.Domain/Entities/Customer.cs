namespace TILSOFTAI.Domain.Entities;

public sealed class Customer
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
}
