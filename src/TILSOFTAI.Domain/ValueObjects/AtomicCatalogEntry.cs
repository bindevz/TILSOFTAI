namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// Governing metadata for an AI-allowed stored procedure.
/// </summary>
public sealed record AtomicCatalogEntry(
    string SpName,
    bool IsEnabled,
    bool IsReadOnly,
    bool IsAtomicCompatible,
    string? Domain,
    string? Entity,
    string IntentVi,
    string? IntentEn,
    string? Tags,
    string? ParamsJson,
    string? ExampleJson,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// A ranked search hit for the stored procedure catalog.
/// </summary>
public sealed record AtomicCatalogSearchHit(
    string SpName,
    string? Domain,
    string? Entity,
    string IntentVi,
    string? IntentEn,
    string? Tags,
    int Score,
    string? ParamsJson,
    string? ExampleJson);
