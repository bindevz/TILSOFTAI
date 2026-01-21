using System.Text.Json;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Llm.OpenAi;

/// <summary>
/// Builds OpenAI "tools" JSON schema from ToolInputSpecCatalog.
/// This keeps Orchestrator logic dynamic (no keyword heuristic in C#).
/// </summary>
public sealed class OpenAiToolSchemaFactory
{
    private readonly ToolRegistry _toolRegistry;
    private readonly ToolInputSpecCatalog _specCatalog;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public OpenAiToolSchemaFactory(ToolRegistry toolRegistry, ToolInputSpecCatalog specCatalog)
    {
        _toolRegistry = toolRegistry;
        _specCatalog = specCatalog;
    }

    public IReadOnlyList<OpenAiToolDefinition> BuildTools(IEnumerable<string> toolNames)
    {
        var list = new List<OpenAiToolDefinition>();

        foreach (var name in toolNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!_toolRegistry.IsWhitelisted(name))
                continue;

            var parameters = BuildParametersSchema(name);

            list.Add(new OpenAiToolDefinition
            {
                Type = "function",
                Function = new OpenAiFunctionDefinition
                {
                    Name = name,
                    Description = $"Invoke tool '{name}'.",
                    Parameters = parameters
                }
            });
        }

        return list;
    }

    private JsonElement BuildParametersSchema(string toolName)
    {
        if (!_specCatalog.TryGet(toolName, out var spec))
        {
            // Fail-open: accept any object.
            using var doc = JsonDocument.Parse("{\"type\":\"object\",\"additionalProperties\":true}");
            return doc.RootElement.Clone();
        }

        var props = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var required = new List<string>();

        if (spec.AllowedFilterKeys.Count > 0)
        {
            props["filters"] = new
            {
                type = "object",
                additionalProperties = new { type = "string" }
            };
        }

        if (spec.SupportsPaging)
        {
            props["page"] = new { type = "integer", minimum = 1, default_ = spec.DefaultPage };
            props["pageSize"] = new { type = "integer", minimum = 1, maximum = spec.MaxPageSize, default_ = spec.DefaultPageSize };
        }

        foreach (var (argName, argSpec) in spec.Args)
        {
            props[argName] = ArgToSchema(argSpec);
            if (argSpec.Required) required.Add(argName);
        }

        // Build schema node and return as JsonElement.
        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["properties"] = props,
            ["additionalProperties"] = false
        };

        if (required.Count > 0)
            schema["required"] = required;

        // Note: JSON schema does not allow property name "default_"; replace.
        var json = JsonSerializer.Serialize(schema, _json).Replace("\"default_\"", "\"default\"");
        using var doc2 = JsonDocument.Parse(json);
        return doc2.RootElement.Clone();
    }

    private static object ArgToSchema(ToolArgSpec arg)
    {
        return arg.Type switch
        {
            ToolArgType.String => new { type = "string" },
            ToolArgType.Guid => new { type = "string", format = "uuid" },
            ToolArgType.Bool => new { type = "boolean" },
            ToolArgType.Int => new { type = "integer", minimum = arg.MinInt, maximum = arg.MaxInt, @default = arg.Default },
            ToolArgType.Decimal => new { type = "number" },
            ToolArgType.StringMap => new { type = "object", additionalProperties = new { type = "string" } },
            ToolArgType.Json => new { type = "object", additionalProperties = true },
            _ => new { type = "object", additionalProperties = true }
        };
    }
}
