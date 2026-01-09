namespace TILSOFTAI.Domain.ValueObjects;

public sealed record PriceAnalysis(decimal BasePrice, decimal Adjustment, decimal FinalPrice);
