using System.Text.Json.Nodes;

namespace TILSOFTAI.Contracts.Tools;

public sealed record CapabilityDescriptor(
    Guid CapabilityId,
    string ModuleCode,
    string CapabilityCode,
    string CapabilityName,
    string Description,
    string RequiredPermissionCode,
    ToolDescriptor Tool);

public sealed record ToolDescriptor(
    Guid ToolId,
    string ToolName,
    string SqlProcedureName,
    string InputJsonSchema,
    int MaxRows,
    int TimeoutMs,
    string RequiredPermissionCode);

public sealed record ToolExecutionRequest(ToolDescriptor Tool, JsonObject Parameters);

public sealed record ToolExecutionResult(
    string ToolName,
    string CapabilityCode,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyList<string> Filters,
    TimeSpan Elapsed);

