using System.Text.Json.Serialization;

namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// Column-level semantics carried alongside TabularData.
///
/// Design goals:
/// - Deterministic: semantics must come from SQL metadata (ResultSet-0), not LLM inference.
/// - Lightweight: only fields needed for safe column interpretation.
/// - Tool-friendly: serialized as part of tool payloads.
/// </summary>
public sealed record TabularColumnSemantic(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("vi")] string? Vi = null,
    [property: JsonPropertyName("en")] string? En = null,
    [property: JsonPropertyName("role")] string? Role = null,
    [property: JsonPropertyName("unit")] string? Unit = null,
    [property: JsonPropertyName("notes")] string? Notes = null);

public sealed record TabularSchema(
    [property: JsonPropertyName("columns")] IReadOnlyList<TabularColumnSemantic> Columns,
    [property: JsonPropertyName("unknownColumns")] IReadOnlyList<string> UnknownColumns);
