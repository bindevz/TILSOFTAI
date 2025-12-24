namespace TILSOFTAI.Domain.Entities;

public sealed class ProductModel
{
    public Guid Id { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<ProductModelAttribute> Attributes { get; set; } = new List<ProductModelAttribute>();
}

public sealed class ProductModelAttribute
{
    public Guid Id { get; init; }
    public Guid ModelId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public ProductModel Model { get; set; } = null!;
}
