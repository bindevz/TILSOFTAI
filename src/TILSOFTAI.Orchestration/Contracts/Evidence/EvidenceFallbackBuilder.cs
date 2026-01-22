using System.Text.Json;

namespace TILSOFTAI.Orchestration.Contracts.Evidence;

/// <summary>
/// Builds a compact evidence item from an arbitrary tool payload.
///
/// Why: Some clients/UI layers prefer (or only render) envelope.evidence.
/// If a handler forgets to attach evidence, the LLM can get "empty" signals and loop.
///
/// Design goals:
/// - Generic (no tool-specific hard-coding)
/// - Bounded (avoid token bloat)
/// - Stable enough for downstream parsing
/// </summary>
public static class EvidenceFallbackBuilder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<EnvelopeEvidenceItemV1> Build(object? payload,
        int maxDepth = 3,
        int maxArrayItems = 5,
        int maxProperties = 20,
        int maxStringLen = 512)
    {
        var compact = Compact(payload, maxDepth, maxArrayItems, maxProperties, maxStringLen);

        return
        [
            new EnvelopeEvidenceItemV1
            {
                Id = "fallback_payload",
                Type = "payload",
                Title = "Tool output (fallback)",
                Payload = compact
            }
        ];
    }

    private static object Compact(object? payload, int maxDepth, int maxArrayItems, int maxProperties, int maxStringLen)
    {
        if (payload is null)
            return new { empty = true };

        // Materialize to a JsonElement so we can inspect generically.
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload, Json));
        var root = doc.RootElement;
        return CompactElement(root, depth: 0, maxDepth, maxArrayItems, maxProperties, maxStringLen);
    }

    private static object CompactElement(JsonElement el, int depth, int maxDepth, int maxArrayItems, int maxProperties, int maxStringLen)
    {
        if (depth >= maxDepth)
        {
            return el.ValueKind switch
            {
                JsonValueKind.Array => new { type = "array", count = el.GetArrayLength() },
                JsonValueKind.Object => new { type = "object" },
                _ => CompactPrimitive(el, maxStringLen)
            };
        }

        return el.ValueKind switch
        {
            JsonValueKind.Object => CompactObject(el, depth, maxDepth, maxArrayItems, maxProperties, maxStringLen),
            JsonValueKind.Array => CompactArray(el, depth, maxDepth, maxArrayItems, maxProperties, maxStringLen),
            _ => CompactPrimitive(el, maxStringLen)
        };
    }

    private static object CompactObject(JsonElement el, int depth, int maxDepth, int maxArrayItems, int maxProperties, int maxStringLen)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var i = 0;

        foreach (var prop in el.EnumerateObject())
        {
            if (i++ >= maxProperties)
            {
                dict["__truncated__"] = true;
                break;
            }

            dict[prop.Name] = CompactElement(prop.Value, depth + 1, maxDepth, maxArrayItems, maxProperties, maxStringLen);
        }

        // If payload follows our common "kind/schemaVersion" convention, keep them easy to read.
        if (!dict.ContainsKey("kind") && el.TryGetProperty("kind", out var kindEl) && kindEl.ValueKind == JsonValueKind.String)
            dict["kind"] = kindEl.GetString();

        if (!dict.ContainsKey("schemaVersion") && el.TryGetProperty("schemaVersion", out var verEl) && verEl.ValueKind == JsonValueKind.Number)
            dict["schemaVersion"] = verEl.GetInt32();

        return dict;
    }

    private static object CompactArray(JsonElement el, int depth, int maxDepth, int maxArrayItems, int maxProperties, int maxStringLen)
    {
        var len = el.GetArrayLength();
        var take = Math.Min(len, Math.Max(0, maxArrayItems));
        var items = new List<object?>(take);

        for (var i = 0; i < take; i++)
        {
            items.Add(CompactElement(el[i], depth + 1, maxDepth, maxArrayItems, maxProperties, maxStringLen));
        }

        return new
        {
            type = "array",
            count = len,
            preview = items
        };
    }

    private static object? CompactPrimitive(JsonElement el, int maxStringLen)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String => Truncate(el.GetString(), maxStringLen),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => Truncate(el.ToString(), maxStringLen)
        };
    }

    private static string? Truncate(string? s, int maxLen)
    {
        if (s is null)
            return null;

        if (maxLen <= 0)
            return string.Empty;

        if (s.Length <= maxLen)
            return s;

        return s.Substring(0, maxLen) + "...";
    }}

