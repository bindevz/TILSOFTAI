using System.Text.Json;
using System.Text.Json.Serialization;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Llm;

public sealed class ResponseParser
{
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public ToolInvocation? Parse(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawContent);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("tool", out var toolElement) || toolElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            if (!root.TryGetProperty("arguments", out var argumentsElement) || argumentsElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new ToolInvocation(toolElement.GetString()!, argumentsElement.Clone());
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
