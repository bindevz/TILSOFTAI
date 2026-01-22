using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Configuration;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ToolResultCompactor
{
    private const int DefaultMaxBytes = 12000;
    private const string TruncationWarning = "tool_result_truncated";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string CompactEnvelopeJson(string envelopeJson, int maxBytes = DefaultMaxBytes, CompactionLimitsSettings? limits = null)
        => CompactEnvelopeJsonWithMetadata(envelopeJson, maxBytes, limits).Json;

    public static ToolCompactionResult CompactEnvelopeJsonWithMetadata(string envelopeJson, int maxBytes = DefaultMaxBytes, CompactionLimitsSettings? limits = null)
    {
        maxBytes = Math.Clamp(maxBytes, 1000, 200000);
        var maxDepth = Math.Clamp(limits?.MaxDepth ?? 6, 1, 20);
        var maxArrayElements = Math.Clamp(limits?.MaxArrayElements ?? 20, 1, 200);
        var maxStringLength = Math.Clamp(limits?.MaxStringLength ?? 500, 50, 5000);

        var droppedFields = new List<string>();

        if (string.IsNullOrWhiteSpace(envelopeJson))
            return BuildMinimalEnvelopeResult(null, note: "empty", maxBytes, droppedFields);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(envelopeJson);
        }
        catch
        {
            return BuildMinimalEnvelopeResult(null, note: "invalid_json", maxBytes, droppedFields);
        }

        if (root is not JsonObject obj)
            return BuildMinimalEnvelopeResult(null, note: "invalid_root", maxBytes, droppedFields);

        var compacted = false;
        var truncated = false;

        if (obj.Remove("data"))
        {
            compacted = true;
            droppedFields.Add("data");
        }

        if (obj.TryGetPropertyValue("evidence", out var evidenceNode) && evidenceNode is not null)
        {
            var pruned = PruneNode(evidenceNode, 0, maxDepth, maxArrayElements, maxStringLength, ref truncated);
            obj["evidence"] = pruned;
            compacted = true;
        }

        if (compacted)
            obj["compacted"] = true;
        if (truncated)
            obj["truncated"] = true;

        var compaction = new JsonObject
        {
            ["truncated"] = truncated,
            ["droppedFields"] = new JsonArray(droppedFields.Select(d => JsonValue.Create(d)).ToArray())
        };
        obj["compaction"] = compaction;
        if (truncated)
            AppendTruncationWarning(obj);

        var json = obj.ToJsonString(Json);
        if (GetByteCount(json) <= maxBytes)
            return FinalizeCompaction(obj, compaction, droppedFields, truncated, json, maxBytes);

        truncated = true;
        obj["truncated"] = true;
        obj["compacted"] = true;

        if (obj.ContainsKey("evidence"))
        {
            obj["evidence"] = new JsonArray();
            if (!droppedFields.Contains("evidence"))
                droppedFields.Add("evidence");
        }

        compaction["truncated"] = true;
        compaction["droppedFields"] = new JsonArray(droppedFields.Select(d => JsonValue.Create(d)).ToArray());
        AppendTruncationWarning(obj);

        json = obj.ToJsonString(Json);
        if (GetByteCount(json) <= maxBytes)
            return FinalizeCompaction(obj, compaction, droppedFields, true, json, maxBytes);

        return BuildMinimalEnvelopeResult(obj, note: "max_bytes", maxBytes, droppedFields);
    }

    public static string CompactEnvelope(EnvelopeV1 env, int maxBytes = DefaultMaxBytes, CompactionLimitsSettings? limits = null)
        => CompactEnvelopeWithMetadata(env, maxBytes, limits).Json;

    public static ToolCompactionResult CompactEnvelopeWithMetadata(EnvelopeV1 env, int maxBytes = DefaultMaxBytes, CompactionLimitsSettings? limits = null)
    {
        var json = JsonSerializer.Serialize(env, Json);
        return CompactEnvelopeJsonWithMetadata(json, maxBytes, limits);
    }

    private static ToolCompactionResult FinalizeCompaction(
        JsonObject obj,
        JsonObject compaction,
        IReadOnlyList<string> droppedFields,
        bool truncated,
        string jsonWithoutHash,
        int maxBytes)
    {
        var hash = ComputeSha256(jsonWithoutHash);
        compaction["outputHash"] = hash;

        var finalJson = obj.ToJsonString(Json);
        if (GetByteCount(finalJson) > maxBytes)
            return BuildMinimalEnvelopeResult(obj, note: "max_bytes", maxBytes, droppedFields);

        return new ToolCompactionResult(finalJson, truncated, droppedFields, hash, GetByteCount(finalJson));
    }

    private static ToolCompactionResult BuildMinimalEnvelopeResult(
        JsonObject? source,
        string note,
        int maxBytes,
        IReadOnlyList<string> droppedFields)
    {
        var minimal = new JsonObject
        {
            ["compacted"] = true,
            ["truncated"] = true,
            ["note"] = note
        };
        if (string.Equals(note, "max_bytes", StringComparison.OrdinalIgnoreCase))
        {
            minimal["warnings"] = new JsonArray(TruncationWarning);
        }

        if (source is not null)
        {
            if (source.TryGetPropertyValue("tool", out var tool))
                minimal["tool"] = tool?.DeepClone();
            if (source.TryGetPropertyValue("ok", out var ok))
                minimal["ok"] = ok?.DeepClone();
            if (source.TryGetPropertyValue("message", out var msg) && msg is JsonValue msgVal && msgVal.TryGetValue<string>(out var s))
                minimal["message"] = TruncateString(s, 200);
        }

        var compaction = new JsonObject
        {
            ["truncated"] = true,
            ["droppedFields"] = new JsonArray(droppedFields.Select(d => JsonValue.Create(d)).ToArray())
        };
        minimal["compaction"] = compaction;

        var jsonWithoutHash = minimal.ToJsonString(Json);
        var hash = ComputeSha256(jsonWithoutHash);
        compaction["outputHash"] = hash;

        var json = minimal.ToJsonString(Json);
        if (GetByteCount(json) <= maxBytes)
            return new ToolCompactionResult(json, true, droppedFields, hash, GetByteCount(json));

        var fallback = "{\"compacted\":true,\"truncated\":true}";
        var fallbackHash = ComputeSha256(fallback);
        return new ToolCompactionResult(fallback, true, droppedFields, fallbackHash, GetByteCount(fallback));
    }

    private static JsonNode PruneNode(JsonNode node, int depth, int maxDepth, int maxArrayElements, int maxStringLength, ref bool truncated)
    {
        if (depth >= maxDepth)
        {
            truncated = true;
            return JsonValue.Create("[truncated]")!;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var s))
            {
                if (s.Length > maxStringLength)
                {
                    truncated = true;
                    s = s.Substring(0, maxStringLength) + "...";
                }
                return JsonValue.Create(s)!;
            }

            return value.DeepClone();
        }

        if (node is JsonArray arr)
        {
            var result = new JsonArray();
            var take = Math.Min(arr.Count, maxArrayElements);
            for (var i = 0; i < take; i++)
            {
                if (arr[i] is null)
                {
                    result.Add(null);
                    continue;
                }
                result.Add(PruneNode(arr[i]!, depth + 1, maxDepth, maxArrayElements, maxStringLength, ref truncated));
            }
            if (arr.Count > maxArrayElements)
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
                result[kv.Key] = PruneNode(kv.Value, depth + 1, maxDepth, maxArrayElements, maxStringLength, ref truncated);
            }
            return result;
        }

        return node.DeepClone();
    }

    private static void AppendTruncationWarning(JsonObject obj)
    {
        AppendWarning(obj, TruncationWarning);
        AppendEvidenceWarning(obj, TruncationWarning);
    }

    private static void AppendWarning(JsonObject obj, string warning)
    {
        if (!obj.TryGetPropertyValue("warnings", out var warningsNode) || warningsNode is not JsonArray warnings)
        {
            warnings = new JsonArray();
            obj["warnings"] = warnings;
        }

        foreach (var node in warnings)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var existing) &&
                string.Equals(existing, warning, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        warnings.Add(warning);
    }

    private static void AppendEvidenceWarning(JsonObject obj, string warning)
    {
        if (!obj.TryGetPropertyValue("evidence", out var evidenceNode) || evidenceNode is not JsonArray evidence)
            return;

        JsonObject? warningsItem = null;
        foreach (var item in evidence)
        {
            if (item is not JsonObject itemObj)
                continue;
            if (!itemObj.TryGetPropertyValue("id", out var idNode) || idNode is not JsonValue idValue)
                continue;
            if (!idValue.TryGetValue<string>(out var id))
                continue;
            if (string.Equals(id, "warnings", StringComparison.OrdinalIgnoreCase))
            {
                warningsItem = itemObj;
                break;
            }
        }

        if (warningsItem is null)
        {
            warningsItem = new JsonObject
            {
                ["id"] = "warnings",
                ["type"] = "metric",
                ["title"] = "Warnings",
                ["payload"] = new JsonObject
                {
                    ["warnings"] = new JsonArray(warning)
                }
            };
            evidence.Add(warningsItem);
            return;
        }

        if (!warningsItem.TryGetPropertyValue("payload", out var payloadNode) || payloadNode is not JsonObject payloadObj)
        {
            payloadObj = new JsonObject();
            warningsItem["payload"] = payloadObj;
        }

        if (!payloadObj.TryGetPropertyValue("warnings", out var warnNode) || warnNode is not JsonArray warnArray)
        {
            warnArray = new JsonArray();
            payloadObj["warnings"] = warnArray;
        }

        foreach (var node in warnArray)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var existing) &&
                string.Equals(existing, warning, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        warnArray.Add(warning);
    }

    private static string TruncateString(string input, int max)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Length <= max ? input : input.Substring(0, max) + "...";
    }

    private static int GetByteCount(string text) => Encoding.UTF8.GetByteCount(text);

    private static string ComputeSha256(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}

public sealed record ToolCompactionResult(
    string Json,
    bool Truncated,
    IReadOnlyList<string> DroppedFields,
    string OutputHash,
    int Bytes);
