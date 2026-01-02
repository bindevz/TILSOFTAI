using System.Text.Json;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

internal static class SchemaParsing
{
    public static IReadOnlyDictionary<string, string?> ReadFilters(JsonElement args)
    {
        var filters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (args.ValueKind != JsonValueKind.Object)
            return filters;

        if (!args.TryGetProperty("filters", out var f) || f.ValueKind != JsonValueKind.Object)
            return filters;

        foreach (var p in f.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String)
                filters[p.Name] = string.IsNullOrWhiteSpace(p.Value.GetString()) ? null : p.Value.GetString();
            else if (p.Value.ValueKind == JsonValueKind.Number)
                filters[p.Name] = p.Value.ToString();
            else if (p.Value.ValueKind == JsonValueKind.True || p.Value.ValueKind == JsonValueKind.False)
                filters[p.Name] = p.Value.GetBoolean() ? "true" : "false";
            else if (p.Value.ValueKind == JsonValueKind.Null)
                filters[p.Name] = null;
        }

        return filters;
    }

    public static int ReadInt(JsonElement args, string prop, int @default)
    {
        if (args.ValueKind != JsonValueKind.Object)
            return @default;

        if (!args.TryGetProperty(prop, out var v))
            return @default;

        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
            return n;

        if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var s))
            return s;

        return @default;
    }

    public static bool ReadBool(JsonElement args, string prop, bool @default)
    {
        if (args.ValueKind != JsonValueKind.Object)
            return @default;

        if (!args.TryGetProperty(prop, out var v))
            return @default;

        if (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            return v.GetBoolean();

        if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b))
            return b;

        return @default;
    }
}
