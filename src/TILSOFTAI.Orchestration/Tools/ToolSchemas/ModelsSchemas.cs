using System.Text.Json;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

public static class ModelsSchemas
{
    public static ValidationResult<ModelSearchIntent> ValidateSearch(JsonElement args)
    {
        var rangeName = GetString(args, "rangeName") ?? GetString(args, "category");
        var modelCode = GetString(args, "modelCode");
        var modelName = GetString(args, "modelName") ?? GetString(args, "name");
        var season = GetString(args, "season");
        var collection = GetString(args, "collection");
        var page = RequireInt(args, "page");
        var size = RequireInt(args, "pageSize");
        return ValidationResult<ModelSearchIntent>.Success(new ModelSearchIntent(rangeName, modelCode, modelName, season, collection, page, size));
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
        var attributes = RequireDict(args, "attributes");
        return ValidationResult<ModelCreatePrepareIntent>.Success(new ModelCreatePrepareIntent(name, category, basePrice, attributes));
    }

    public static ValidationResult<ModelCreateCommitIntent> ValidateCreateCommit(JsonElement args)
    {
        var confirmationId = RequireString(args, "confirmationId");
        return ValidationResult<ModelCreateCommitIntent>.Success(new ModelCreateCommitIntent(confirmationId));
    }

    private static string? GetString(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.String)
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
        if (element.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var id))
        {
            return id;
        }
        throw new ArgumentException($"{prop} is required and must be a GUID.");
    }

    private static int RequireInt(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }
        throw new ArgumentException($"{prop} is required and must be an integer.");
    }

    private static decimal RequireDecimal(JsonElement element, string prop)
    {
        if (element.TryGetProperty(prop, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }
        throw new ArgumentException($"{prop} is required and must be a decimal.");
    }

    private static IReadOnlyDictionary<string, string> RequireDict(JsonElement element, string prop)
    {
        if (!element.TryGetProperty(prop, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"{prop} is required and must be an object.");
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
