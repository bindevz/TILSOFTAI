namespace TILSOFTAI.Domain.ValueObjects;

/// <summary>
/// Query result used by models.options tool (contract v1).
/// Options are expressed as groups with allowed values (and optional constraints)
/// so the LLM can reliably validate a natural-language spec.
/// </summary>
public sealed record ModelsOptionsResult(
    ModelHeader Model,
    IReadOnlyList<OptionGroup> OptionGroups,
    IReadOnlyList<OptionConstraint> Constraints);

public sealed record ModelHeader(
    int ModelId,
    string ModelCode,
    string ModelName,
    string? Season,
    string? Collection,
    string? RangeName);

public sealed record OptionGroup(
    string GroupKey,
    string GroupName,
    bool IsRequired,
    int SortOrder,
    IReadOnlyList<OptionValue> Values);

public sealed record OptionValue(
    string ValueKey,
    string ValueName,
    int SortOrder,
    string? Note = null);

public sealed record OptionConstraint(
    string RuleType,
    string IfGroupKey,
    string IfValueKey,
    string ThenGroupKey,
    string ThenValueKey,
    string? Message = null);
