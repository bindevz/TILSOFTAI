using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TILSOFTAI.Orchestration.Contracts;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ToolResultCompactor
{
    private const int DefaultMaxBytes = 12000;
    private const int MaxDepth = 6;
    private const int MaxArrayElements = 20;
    private const int MaxStringLength = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string CompactEnvelopeJson(string envelopeJson, int maxBytes = DefaultMaxBytes)
    {
        maxBytes = Math.Clamp(maxBytes, 1000, 200000);

        if (string.IsNullOrWhiteSpace(envelopeJson))
            return BuildMinimalEnvelope(null, note: "empty", maxBytes);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(envelopeJson);
        }
        catch
        {
            return BuildMinimalEnvelope(null, note: "invalid_json", maxBytes);
        }

        if (root is not JsonObject obj)
            return BuildMinimalEnvelope(null, note: "invalid_root", maxBytes);

        var compacted = false;
        var truncated = false;

        if (obj.Remove("data"))
            compacted = true;

        if (obj.TryGetPropertyValue("evidence", out var evidenceNode) && evidenceNode is not null)
        {
            var pruned = PruneNode(evidenceNode, 0, ref truncated);
            obj["evidence"] = pruned;
            compacted = true;
        }

        if (compacted)
            obj["compacted"] = true;
        if (truncated)
            obj["truncated"] = true;

        var json = obj.ToJsonString(Json);
        if (GetByteCount(json) <= maxBytes)
            return json;

        truncated = true;
        obj["truncated"] = true;
        obj["compacted"] = true;

        if (obj.ContainsKey("evidence"))
            obj["evidence"] = new JsonArray();

        json = obj.ToJsonString(Json);
        if (GetByteCount(json) <= maxBytes)
            return json;

        return BuildMinimalEnvelope(obj, note: "max_bytes", maxBytes);
    }

    public static string CompactEnvelope(EnvelopeV1 env, int maxBytes = DefaultMaxBytes)
    {
        var json = JsonSerializer.Serialize(env, Json);
        return CompactEnvelopeJson(json, maxBytes);
    }

    private static JsonNode PruneNode(JsonNode node, int depth, ref bool truncated)
    {
        if (depth >= MaxDepth)
        {
            truncated = true;
            return JsonValue.Create("[truncated]")!;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var s))
            {
                if (s.Length > MaxStringLength)
                {
                    truncated = true;
                    s = s.Substring(0, MaxStringLength) + "...";
                }
                return JsonValue.Create(s)!;
            }

            return value.DeepClone();
        }

        if (node is JsonArray arr)
        {
            var result = new JsonArray();
            var take = Math.Min(arr.Count, MaxArrayElements);
            for (var i = 0; i < take; i++)
            {
                if (arr[i] is null)
                {
                    result.Add(null);
                    continue;
                }
                result.Add(PruneNode(arr[i]!, depth + 1, ref truncated));
            }
            if (arr.Count > MaxArrayElements)
                truncated = true;
            return result;
        }

        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var kv in obj)
            {
                if (kv.Value is null)
                {
                    result[kv.Key] = null;
                    continue;
                }
                result[kv.Key] = PruneNode(kv.Value, depth + 1, ref truncated);
            }
            return result;
        }

        return node.DeepClone();
    }

    private static string BuildMinimalEnvelope(JsonObject? source, string note, int maxBytes)
    {
        var minimal = new JsonObject
        {
            ["compacted"] = true,
            ["truncated"] = true,
            ["note"] = note
        };

        if (source is not null)
        {
            if (source.TryGetPropertyValue("tool", out var tool))
                minimal["tool"] = tool?.DeepClone();
            if (source.TryGetPropertyValue("ok", out var ok))
                minimal["ok"] = ok?.DeepClone();
            if (source.TryGetPropertyValue("message", out var msg) && msg is JsonValue msgVal && msgVal.TryGetValue<string>(out var s))
                minimal["message"] = TruncateString(s, 200);
        }

        var json = minimal.ToJsonString(Json);
        if (GetByteCount(json) <= maxBytes)
            return json;

        return "{\"compacted\":true,\"truncated\":true}";
    }

    private static string TruncateString(string input, int max)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Length <= max ? input : input.Substring(0, max) + "...";
    }

    private static int GetByteCount(string text) => Encoding.UTF8.GetByteCount(text);
}
