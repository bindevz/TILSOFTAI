namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// A single value-hint row returned from the filter-hints stored procedure.
/// </summary>
public sealed record FilterValueHintRow(
    string FilterKey,
    string Value,
    int Count,
    string? Label = null);
