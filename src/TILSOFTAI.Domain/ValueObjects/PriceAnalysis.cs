namespace TILSOFTAI.Domain.ValueObjects;

public sealed record PriceAnalysis(decimal BasePrice, decimal AttributeAdjustment, decimal FinalPrice);
