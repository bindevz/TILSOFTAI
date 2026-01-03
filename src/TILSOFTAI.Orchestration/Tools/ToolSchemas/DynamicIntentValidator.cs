using System.Text.Json;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

internal static class DynamicIntentValidator
{
    public static ValidationResult<DynamicToolIntent> Validate(string toolName, JsonElement args)
    {
        var spec = ToolInputSpecs.For(toolName);

        // Filters
        var rawFilters = SchemaParsing.ReadFilters(args);
        var filters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (spec.AllowedFilterKeys.Count == 0)
        {
            // Tool doesn't accept filters (still keep empty).
        }
        else
        {
            // Only keep keys that are declared in the catalog.
            foreach (var (k, v) in rawFilters)
            {
                if (spec.AllowedFilterKeys.Contains(k))
                    filters[k] = v;
            }
        }

        // Paging
        var page = spec.SupportsPaging
            ? Math.Max(1, SchemaParsing.ReadInt(args, "page", spec.DefaultPage))
            : 1;

        var pageSize = spec.SupportsPaging
            ? Math.Clamp(SchemaParsing.ReadInt(args, "pageSize", spec.DefaultPageSize), 1, spec.MaxPageSize)
            : 1;

        // Scalar args
        var parsedArgs = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in spec.Args)
        {
            var specArg = kv.Value;
            if (TryReadArg(args, specArg, out var value, out var error))
            {
                parsedArgs[specArg.Name] = value;
                continue;
            }

            if (error is not null)
                return ValidationResult<DynamicToolIntent>.Fail(error);

            // Not present
            if (specArg.Required)
                return ValidationResult<DynamicToolIntent>.Fail($"{specArg.Name} is required.");

            parsedArgs[specArg.Name] = specArg.Default;
        }

        return ValidationResult<DynamicToolIntent>.Success(new DynamicToolIntent(filters, page, pageSize, parsedArgs));
    }

    private static bool TryReadArg(JsonElement root, ToolArgSpec argSpec, out object? value, out string? error)
    {
        value = null;
        error = null;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(argSpec.Name, out var v))
            return false;

        switch (argSpec.Type)
        {
            case ToolArgType.String:
                if (v.ValueKind == JsonValueKind.String)
                {
                    var s = v.GetString();
                    value = string.IsNullOrWhiteSpace(s) ? null : s.Trim();
                    return true;
                }
                if (v.ValueKind == JsonValueKind.Null)
                {
                    value = null;
                    return true;
                }
                error = $"{argSpec.Name} must be a string.";
                return false;

            case ToolArgType.Bool:
                if (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
                {
                    value = v.GetBoolean();
                    return true;
                }
                if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var b))
                {
                    value = b;
                    return true;
                }
                error = $"{argSpec.Name} must be a boolean.";
                return false;

            case ToolArgType.Int:
                int n;
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out n))
                {
                    // ok
                }
                else if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n))
                {
                    // ok
                }
                else
                {
                    error = $"{argSpec.Name} must be an integer.";
                    return false;
                }

                if (argSpec.MinInt.HasValue && n < argSpec.MinInt.Value)
                {
                    error = $"{argSpec.Name} must be >= {argSpec.MinInt.Value}.";
                    return false;
                }
                if (argSpec.MaxInt.HasValue && n > argSpec.MaxInt.Value)
                {
                    error = $"{argSpec.Name} must be <= {argSpec.MaxInt.Value}.";
                    return false;
                }
                value = n;
                return true;

            case ToolArgType.Guid:
                if (v.ValueKind == JsonValueKind.String && Guid.TryParse(v.GetString(), out var g))
                {
                    value = g;
                    return true;
                }
                error = $"{argSpec.Name} must be a GUID string.";
                return false;

            case ToolArgType.Decimal:
                decimal d;
                if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out d))
                {
                    value = d;
                    return true;
                }
                if (v.ValueKind == JsonValueKind.String && decimal.TryParse(v.GetString(), out d))
                {
                    value = d;
                    return true;
                }
                error = $"{argSpec.Name} must be a decimal.";
                return false;

            case ToolArgType.StringMap:
                if (v.ValueKind == JsonValueKind.Object)
                {
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in v.EnumerateObject())
                    {
                        if (p.Value.ValueKind != JsonValueKind.String) continue;
                        var s = p.Value.GetString();
                        if (string.IsNullOrWhiteSpace(s)) continue;
                        dict[p.Name] = s.Trim();
                    }

                    value = dict;
                    return true;
                }
                if (v.ValueKind == JsonValueKind.Null)
                {
                    value = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    return true;
                }

                error = $"{argSpec.Name} must be an object of string values.";
                return false;
        }

        error = $"Unsupported argument type for {argSpec.Name}.";
        return false;
    }
}
