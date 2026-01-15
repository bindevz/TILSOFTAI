using System.Text.Json.Serialization;

namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// Fallback signal used to guess table kind when RS0 schema is missing.
/// </summary>
public sealed record TableKindSignalRow(
    [property: JsonPropertyName("tableKind")] string TableKind,
    [property: JsonPropertyName("pattern")] string Pattern,
    [property: JsonPropertyName("weight")] int Weight = 1,
    [property: JsonPropertyName("isRegex")] bool IsRegex = false,
    [property: JsonPropertyName("priority")] int Priority = 0);
