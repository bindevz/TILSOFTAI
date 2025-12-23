using System.Text.Json;

namespace TILSOFTAI.Orchestration.Tools;

public sealed record ToolInvocation(string Tool, JsonElement Arguments);
