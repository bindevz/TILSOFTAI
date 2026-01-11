using System.Text.Json.Serialization;

namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// Composite result for tabular queries that also carry column semantics.
/// </summary>
public sealed record TabularQueryResult(
    [property: JsonPropertyName("table")] TabularData Table,
    [property: JsonPropertyName("schema")] TabularSchema Schema);
