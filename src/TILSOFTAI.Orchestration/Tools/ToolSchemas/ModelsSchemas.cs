using System.Text.Json;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

public static class ModelsSchemas
{
    public static ValidationResult<ModelsSearchIntent> ValidateSearch(JsonElement args)
    {
        var filters = SchemaParsing.ReadFilters(args);
        var page = Math.Max(1, SchemaParsing.ReadInt(args, "page", 1));
        var pageSize = Math.Clamp(SchemaParsing.ReadInt(args, "pageSize", 20), 1, 200);

        return ValidationResult<ModelsSearchIntent>.Success(new ModelsSearchIntent(filters, page, pageSize));
    }

    public static ValidationResult<ModelsCountIntent> ValidateCount(JsonElement args)
    {
        var filters = SchemaParsing.ReadFilters(args);
        return ValidationResult<ModelsCountIntent>.Success(new ModelsCountIntent(filters));
    }

    public static ValidationResult<ModelGetIntent> ValidateGet(JsonElement args)
    {
        var id = RequireGuid(args, "modelId");
        return ValidationResult<ModelGetIntent>.Success(new ModelGetIntent(id));
    }

    public static ValidationResult<ModelListAttributesIntent> ValidateAttributes(JsonElement args)
    {
        var id = RequireGuid(args, "modelId");
        return ValidationResult<ModelListAttributesIntent>.Success(new ModelListAttributesIntent(id));
    }

    public static ValidationResult<ModelPriceAnalyzeIntent> ValidatePrice(JsonElement args)
    {
        var id = RequireGuid(args, "modelId");
        return ValidationResult<ModelPriceAnalyzeIntent>.Success(new ModelPriceAnalyzeIntent(id));
    }

    public static ValidationResult<ModelCreatePrepareIntent> ValidateCreatePrepare(JsonElement args)
    {
        var name = RequireString(args, "name");
        var category = RequireString(args, "category");
        var basePrice = RequireDecimal(args, "basePrice");
        var attributes = ReadDict(args, "attributes");
        return ValidationResult<ModelCreatePrepareIntent>.Success(new ModelCreatePrepareIntent(name, category, basePrice, attributes));
    }

    public static ValidationResult<ModelCreateCommitIntent> ValidateCreateCommit(JsonElement args)
    {
        var confirmationId = RequireString(args, "confirmationId");
        return ValidationResult<ModelCreateCommitIntent>.Success(new ModelCreateCommitIntent(confirmationId));
    }

    private static string? GetString(JsonElement element, string prop)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }
        return null;
    }

    private static string RequireString(JsonElement element, string prop)
    {
        var val = GetString(element, prop);
        if (string.IsNullOrWhiteSpace(val))
        {
            throw new ArgumentException($"{prop} is required.");
        }
        return val;
    }

    private static Guid RequireGuid(JsonElement element, string prop)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id))
        {
            return id;
        }
        throw new ArgumentException($"{prop} is required and must be a GUID.");
    }

    private static decimal RequireDecimal(JsonElement element, string prop)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(prop, out var value))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var num))
                return num;
            if (value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), out var parsed))
                return parsed;
        }

        throw new ArgumentException($"{prop} is required and must be a decimal.");
    }

    private static IReadOnlyDictionary<string, string> ReadDict(JsonElement element, string prop)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(prop, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in value.EnumerateObject())
        {
            if (item.Value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.Value.GetString()))
            {
                dict[item.Name] = item.Value.GetString()!;
            }
        }
        return dict;
    }
}
