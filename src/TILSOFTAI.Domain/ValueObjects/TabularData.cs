namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// A lightweight, schema-carrying tabular dataset intended for analytics workloads.
///
/// Design goals:
/// - No domain entities / DTOs.
/// - Stable, generic contract across data sources.
/// - Efficient to materialize into DataFrames or stream to clients as previews.
/// </summary>
public sealed record TabularData(
    IReadOnlyList<TabularColumn> Columns,
    IReadOnlyList<object?[]> Rows,
    int? TotalCount = null);

public sealed record TabularColumn(
    string Name,
    TabularType Type);

public enum TabularType
{
    String = 0,
    Int32 = 1,
    Double = 2,
    Decimal = 3,
    Boolean = 4,
    DateTime = 5
}
