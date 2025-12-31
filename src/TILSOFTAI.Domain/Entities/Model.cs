namespace TILSOFTAI.Domain.Entities;

public sealed class Model
{
    public int ModelID { get; init; }
    public string TenantId { get; init; } = string.Empty;
    public string ModelUD { get; set; } = string.Empty;
    public string ModelNM { get; set; } = string.Empty;
    public string Season { get; set; } = string.Empty;
    public string Collection { get; set; } = string.Empty;
    public string RangeName { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
    public ICollection<ModelAttribute> Attributes { get; set; } = new List<ModelAttribute>();
}

public sealed class ModelAttribute
{
    public Guid Id { get; init; }
    public Guid ModelId { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public Model Model { get; set; } = null!;
}
