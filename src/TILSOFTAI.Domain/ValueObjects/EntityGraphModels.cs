using System.Text.Json.Serialization;

namespace TILSOFTAI.Domain.ValueObjects;

public sealed record EntityGraphPackHint(
    int GraphId,
    string PackCode,
    string PackType,
    string SpName,
    string? Tags,
    int SortOrder
);

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
    IReadOnlyList<EntityGraphPackHint> Packs
);


public sealed record EntityGraphDefinition(
    EntityGraphSummary Summary,
    IReadOnlyList<EntityGraphPackSummary> Packs,
    IReadOnlyList<EntityGraphNode> Nodes,
    IReadOnlyList<EntityGraphEdge> Edges,
    IReadOnlyList<EntityGraphGlossaryEntry> Glossary
);

public sealed record EntityGraphSummary(
    int GraphId,
    string GraphCode,
    string? Domain,
    string? Entity,
    string? Tags,
    string? RootSpName,
    string? DescriptionVi,
    string? DescriptionEn,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset CreatedAtUtc
);

public sealed record EntityGraphPackSummary(
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
    int SortOrder
);

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
    string? Notes
);

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
    string? Notes
);

public sealed record EntityGraphGlossaryEntry(
    int GlossaryId,
    int GraphId,
    string Lang,
    string Term,
    string Canonical,
    string? Notes
);
