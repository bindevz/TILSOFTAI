namespace TILSOFTAI.Orchestration.Tools;

using System.Text.Json;

internal static class DynamicToolIntentExtensions
{
    public static string? GetString(this DynamicToolIntent intent, string key)
        => intent.Args.TryGetValue(key, out var v) ? v as string : null;

    public static string GetStringRequired(this DynamicToolIntent intent, string key)
    {
        var s = intent.GetString(key);
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException($"{key} is required.");
        return s;
    }

    public static int GetInt(this DynamicToolIntent intent, string key, int @default = 0)
    {
        if (!intent.Args.TryGetValue(key, out var v) || v is null)
            return @default;
        return v is int i ? i : @default;
    }

    public static bool GetBool(this DynamicToolIntent intent, string key, bool @default = false)
    {
        if (!intent.Args.TryGetValue(key, out var v) || v is null)
            return @default;
        return v is bool b ? b : @default;
    }

    public static Guid GetGuid(this DynamicToolIntent intent, string key)
    {
        if (intent.Args.TryGetValue(key, out var v) && v is Guid g)
            return g;
        throw new ArgumentException($"{key} is required and must be a GUID.");
    }

    public static decimal GetDecimal(this DynamicToolIntent intent, string key, decimal @default = 0)
    {
        if (!intent.Args.TryGetValue(key, out var v) || v is null)
            return @default;
        return v is decimal d ? d : @default;
    }

    public static IReadOnlyDictionary<string, string> GetStringMap(this DynamicToolIntent intent, string key)
    {
        if (!intent.Args.TryGetValue(key, out var v) || v is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (v is IReadOnlyDictionary<string, string> ro)
            return ro;

        if (v is Dictionary<string, string> d)
            return d;

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public static JsonElement? GetJson(this DynamicToolIntent intent, string key)
    {
        if (!intent.Args.TryGetValue(key, out var v) || v is null)
            return null;

        if (v is JsonElement je)
            return je;

        return null;
    }

    public static JsonElement GetJsonRequired(this DynamicToolIntent intent, string key)
    {
        var je = intent.GetJson(key);
        if (je is null)
            throw new ArgumentException($"{key} is required.");
        return je.Value;
    }
}
