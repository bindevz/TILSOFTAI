namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// Query result used by models.stats tool (contract v1).
/// Stored procedure returns the raw breakdown rows; orchestration maps this to
/// a stable JSON contract for the LLM.
/// </summary>
public sealed record ModelsStatsResult(
    int TotalCount,
    IReadOnlyList<ModelsStatsBreakdown> Breakdowns);

public sealed record ModelsStatsBreakdown(
    string Dimension,
    string? Title,
    IReadOnlyList<ModelsStatsBreakdownItem> Items,
    int? OtherCount = null);

public sealed record ModelsStatsBreakdownItem(
    string Key,
    string? Label,
    int Count);
