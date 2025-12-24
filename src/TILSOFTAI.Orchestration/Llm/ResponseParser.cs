using System.Text.Json;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Llm;

public sealed class ResponseParser
{
    private static readonly HashSet<string> AllowedRootKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "tool",
        "arguments"
    };

    public ToolInvocation Parse(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            throw new ResponseContractException("Response is empty.");
        }

        var jsonPayload = ExtractSingleJsonObject(rawContent);

        try
        {
            using var document = JsonDocument.Parse(jsonPayload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });

            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ResponseContractException("Response must be a JSON object.");
            }

            var rootKeys = root.EnumerateObject().Select(p => p.Name).ToArray();
            if (rootKeys.Except(AllowedRootKeys, StringComparer.OrdinalIgnoreCase).Any())
            {
                throw new ResponseContractException("Response contains unexpected fields.");
            }

            if (!root.TryGetProperty("tool", out var toolElement) || toolElement.ValueKind != JsonValueKind.String)
            {
                throw new ResponseContractException("Field 'tool' is required and must be a string.");
            }

            var toolName = toolElement.GetString();
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new ResponseContractException("Field 'tool' cannot be empty.");
            }

            if (!root.TryGetProperty("arguments", out var argumentsElement) || argumentsElement.ValueKind != JsonValueKind.Object)
            {
                throw new ResponseContractException("Field 'arguments' is required and must be an object.");
            }

            return new ToolInvocation(toolName, argumentsElement.Clone());
        }
        catch (JsonException)
        {
            throw new ResponseContractException("AI output invalid JSON.");
        }
    }

    private static string ExtractSingleJsonObject(string rawContent)
    {
        var span = rawContent.AsSpan();
        var depth = 0;
        var inString = false;
        var escape = false;
        var foundStart = false;
        var foundEnd = false;
        var startIndex = -1;
        var endIndex = -1;

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];

            if (!foundStart)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (c == '{')
                {
                    foundStart = true;
                    startIndex = i;
                    depth = 1;
                    continue;
                }

                throw new ResponseContractException("AI output invalid JSON.");
            }

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (foundEnd && depth == 0 && !char.IsWhiteSpace(c))
            {
                throw new ResponseContractException("AI output invalid JSON.");
            }

            if (c == '{')
            {
                if (foundEnd && depth == 0)
                {
                    throw new ResponseContractException("AI output invalid JSON.");
                }

                if (!foundStart)
                {
                    foundStart = true;
                    startIndex = i;
                }

                depth++;
                continue;
            }

            if (c == '}')
            {
                if (depth == 0)
                {
                    throw new ResponseContractException("AI output invalid JSON.");
                }

                depth--;
                if (depth == 0)
                {
                    foundEnd = true;
                    endIndex = i;
                }
            }
        }

        if (!foundStart || !foundEnd || depth != 0 || startIndex < 0 || endIndex < startIndex)
        {
            throw new ResponseContractException("AI output invalid JSON.");
        }

        return rawContent.Substring(startIndex, endIndex - startIndex + 1);
    }
}

public sealed class ResponseContractException : Exception
{
    public ResponseContractException(string message) : base(message)
    {
    }
}
