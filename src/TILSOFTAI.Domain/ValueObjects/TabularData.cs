using System.Text.Json.Serialization;

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
    [property: JsonPropertyName("columns")] IReadOnlyList<TabularColumn> Columns,
    [property: JsonPropertyName("rows")] IReadOnlyList<object?[]> Rows,
    [property: JsonPropertyName("totalCount")] int? TotalCount = null);

public sealed record TabularColumn(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] TabularType Type);

public enum TabularType
{
    String = 0,
    Int32 = 1,
    Double = 2,
    Decimal = 3,
    Boolean = 4,
    DateTime = 5
}
