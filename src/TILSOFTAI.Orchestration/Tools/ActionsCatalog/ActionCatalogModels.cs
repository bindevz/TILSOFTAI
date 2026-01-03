namespace TILSOFTAI.Orchestration.Tools.ActionsCatalog;

public sealed record ActionParam(
    string Name,
    string Type,
    bool Required,
    string? Description = null);

public sealed record ActionDescriptor(
    string Action,
    string PrepareTool,
    string CommitTool,
    string Description,
    IReadOnlyList<ActionParam> Parameters,
    object? ExamplePrepareArgs = null,
    object? ExampleCommitArgs = null);
