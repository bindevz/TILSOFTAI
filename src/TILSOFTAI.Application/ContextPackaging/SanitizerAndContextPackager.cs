using System.Text.Json;
using System.Text.Json.Nodes;
using TILSOFTAI.Contracts.Tools;

namespace TILSOFTAI.Application.ContextPackaging;

public static class SanitizerAndContextPackager
{
    public static IReadOnlyList<IReadOnlyDictionary<string, object?>> Sanitize(ToolExecutionResult result)
    {
        return result.Rows
            .Where(row => !row.TryGetValue("IsSensitive", out object? sensitive) || sensitive is not bool flag || !flag)
            .Select(row => row.Where(kvp => kvp.Key is not "IsSensitive").ToDictionary(kvp => kvp.Key, kvp => kvp.Value) as IReadOnlyDictionary<string, object?>)
            .ToList();
    }

    public static JsonObject Build(string question, ToolExecutionResult result, Guid sanitizedArtifactId)
    {
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = Sanitize(result);
        JsonArray importantRows = new(rows.Take(10).Select(row => JsonSerializer.SerializeToNode(row)).ToArray());

        return new JsonObject
        {
            ["question"] = question,
            ["domain"] = "Model",
            ["capability"] = result.CapabilityCode,
            ["toolResults"] = new JsonArray
            {
                new JsonObject
                {
                    ["toolName"] = result.ToolName,
                    ["rowCount"] = rows.Count,
                    ["summary"] = BuildSummary(rows),
                    ["importantRows"] = importantRows,
                    ["schema"] = new JsonArray("ProjectCode:string", "Metric:string", "Value:string"),
                    ["artifactRefs"] = new JsonArray(sanitizedArtifactId.ToString("D"))
                }
            },
            ["provenance"] = new JsonArray
            {
                new JsonObject
                {
                    ["toolName"] = result.ToolName,
                    ["filters"] = new JsonArray(result.Filters.Select(f => JsonValue.Create(f)).ToArray()),
                    ["artifactId"] = sanitizedArtifactId.ToString("D")
                }
            }
        };
    }

    public static int EstimateTokens(JsonObject package) => Math.Max(1, package.ToJsonString().Length / 4);

    private static JsonObject BuildSummary(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        JsonObject summary = [];
        foreach (IReadOnlyDictionary<string, object?> row in rows)
        {
            string key = row.TryGetValue("Metric", out object? metric) ? metric?.ToString() ?? string.Empty : string.Empty;
            string value = row.TryGetValue("Value", out object? rowValue) ? rowValue?.ToString() ?? string.Empty : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
                summary[key.Replace(" ", "", StringComparison.Ordinal)] = value;
        }

        return summary;
    }
}

