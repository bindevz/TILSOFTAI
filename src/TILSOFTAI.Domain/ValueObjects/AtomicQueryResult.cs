using System.Text.Json.Serialization;

namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// Materialized multi-result-set output of an "AtomicQuery" stored procedure.
/// RS0 is schema, RS1 is summary (optional), RS2..N are data tables.
/// </summary>
public sealed record AtomicQueryResult(
    [property: JsonPropertyName("schema")] AtomicQuerySchema Schema,
    [property: JsonPropertyName("summary")] AtomicResultSet? Summary,
    [property: JsonPropertyName("tables")] IReadOnlyList<AtomicResultSet> Tables);

public sealed record AtomicResultSet(
    [property: JsonPropertyName("schema")] AtomicResultSetSchema Schema,
    [property: JsonPropertyName("table")] TabularData Table);
