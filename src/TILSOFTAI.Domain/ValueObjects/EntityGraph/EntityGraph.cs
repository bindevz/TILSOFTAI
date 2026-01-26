namespace TILSOFTAI.Domain.ValueObjects.EntityGraph;

/// <summary>
/// A ranked search hit for the Entity Graph Catalog.
/// </summary>
public sealed record EntityGraphSearchHit(
    int GraphId,
    string GraphCode,
    string? Domain,
    string? Entity,
    string? Tags,
    string? RootSpName,
    string? DescriptionVi,
    string? DescriptionEn,
    int Score,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<EntityGraphPackSummary> Packs);

/// <summary>
/// Lightweight pack metadata for search results.
/// </summary>
public sealed record EntityGraphPackSummary(
    int GraphId,
    string PackCode,
    string PackType,
    string SpName,
    string? Tags,
    int SortOrder,
    string? ParamsJson,
    string? ProducesDatasetsJson);

/// <summary>
/// Full pack metadata for a graph definition.
/// </summary>
public sealed record EntityGraphPack(
    int PackId,
    int GraphId,
    string PackCode,
    string PackType,
    string SpName,
    string? Tags,
    string? IntentVi,
    string? IntentEn,
    string? ParamsJson,
    string? ExampleJson,
    string? ProducesDatasetsJson,
    int SortOrder);

/// <summary>
/// Dataset metadata within a graph.
/// </summary>
public sealed record EntityGraphNode(
    int NodeId,
    int GraphId,
    string DatasetName,
    string? TableKind,
    string? Delivery,
    string? PrimaryKeyJson,
    string? IdColumnsJson,
    string? DimensionHintsJson,
    string? MeasureHintsJson,
    string? TimeColumnsJson,
    string? Notes);

/// <summary>
/// Join hint between datasets.
/// </summary>
public sealed record EntityGraphEdge(
    int EdgeId,
    int GraphId,
    string LeftDataset,
    string RightDataset,
    string LeftKeysJson,
    string RightKeysJson,
    string How,
    string? RightPrefix,
    string? SelectRightJson,
    string? Notes);

/// <summary>
/// Glossary mapping for user terms.
/// </summary>
public sealed record EntityGraphGlossaryEntry(
    int GlossaryId,
    int GraphId,
    string Lang,
    string Term,
    string Canonical,
    string? Notes);

/// <summary>
/// Complete graph definition with packs, nodes, edges, and glossary.
/// </summary>
public sealed record EntityGraphDefinition(
    int GraphId,
    string GraphCode,
    string? Domain,
    string? Entity,
    string? Tags,
    string? RootSpName,
    string? DescriptionVi,
    string? DescriptionEn,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<EntityGraphPack> Packs,
    IReadOnlyList<EntityGraphNode> Nodes,
    IReadOnlyList<EntityGraphEdge> Edges,
    IReadOnlyList<EntityGraphGlossaryEntry> Glossary);
