namespace TILSOFTAI.Orchestration.Tools.ActionsCatalog;

/// <summary>
/// Describes a single parameter for an action definition.
/// </summary>
public sealed record ActionParam(
    string Name,
    string Type,
    bool Required,
    string? Description = null);

/// <summary>
/// Defines a high-level business action that maps to a prepare/commit tool pair.
/// </summary>
public sealed record ActionDescriptor(
    string Action,
    string PrepareTool,
    string CommitTool,
    string Description,
    IReadOnlyList<ActionParam> Parameters,
    object? ExamplePrepareArgs = null,
    object? ExampleCommitArgs = null);
