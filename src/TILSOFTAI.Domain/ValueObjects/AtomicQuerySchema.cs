using System.Text.Json.Serialization;

namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// Multi-result-set schema for "AtomicQuery" style stored procedures (RS0 schema, RS1 summary, RS2..N data).
///
/// RS0 must describe:
/// - Result set level metadata (tableName/tableKind/grain/primaryKey/joinHints/description)
/// - Column level semantics (role/semanticType/unit/format/notes/vi/en)
///
/// The schema is deterministic: it is emitted by SQL (RS0), never inferred by LLM.
/// </summary>
public sealed record AtomicQuerySchema(
    [property: JsonPropertyName("resultSets")] IReadOnlyList<AtomicResultSetSchema> ResultSets);

public sealed record AtomicResultSetSchema(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("tableName")] string TableName,
    [property: JsonPropertyName("tableKind")] string? TableKind = null,
    [property: JsonPropertyName("delivery")] string? Delivery = null, // auto|engine|display|both (optional)
    [property: JsonPropertyName("grain")] string? Grain = null,
    [property: JsonPropertyName("primaryKey")] IReadOnlyList<string>? PrimaryKey = null,
    [property: JsonPropertyName("joinHints")] string? JoinHints = null,
    [property: JsonPropertyName("description_vi")] string? DescriptionVi = null,
    [property: JsonPropertyName("description_en")] string? DescriptionEn = null,
    [property: JsonPropertyName("columns")] IReadOnlyList<AtomicColumnSchema>? Columns = null,
    [property: JsonPropertyName("unknownColumns")] IReadOnlyList<string>? UnknownColumns = null);

public sealed record AtomicColumnSchema(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ordinal")] int? Ordinal = null,
    [property: JsonPropertyName("sqlType")] string? SqlType = null,
    [property: JsonPropertyName("tabularType")] string? TabularType = null,
    [property: JsonPropertyName("role")] string? Role = null,
    [property: JsonPropertyName("semanticType")] string? SemanticType = null,
    [property: JsonPropertyName("unit")] string? Unit = null,
    [property: JsonPropertyName("format")] string? Format = null,
    [property: JsonPropertyName("nullable")] bool? Nullable = null,
    [property: JsonPropertyName("example")] string? Example = null,
    [property: JsonPropertyName("notes")] string? Notes = null,
    [property: JsonPropertyName("vi")] string? Vi = null,
    [property: JsonPropertyName("en")] string? En = null);
