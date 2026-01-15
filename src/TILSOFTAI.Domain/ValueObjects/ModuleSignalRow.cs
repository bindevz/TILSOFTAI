using System.Text.Json.Serialization;

namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// Module routing signals loaded from SQL.
/// </summary>
public sealed record ModuleSignalRow(
    [property: JsonPropertyName("module")] string ModuleName,
    [property: JsonPropertyName("signals")] string? Signals,
    [property: JsonPropertyName("priority")] int Priority = 0);
