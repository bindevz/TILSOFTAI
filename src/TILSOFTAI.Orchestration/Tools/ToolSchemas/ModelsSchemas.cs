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
    public sealed record ModelSearchDynamicIntent(
    IReadOnlyDictionary<string, string?> Filters,
    int Page,
    int PageSize);
    public static ValidationResult<ModelSearchDynamicIntent> ValidateSearchDynamic(JsonElement args)
    {
        var page = 1; // default;
        var pageSize = 20; // default;

        if (RequireInt(args, "page") != 0) { page = RequireInt(args, "page"); };
        if (RequireInt(args, "pageSize") != 0) { pageSize = RequireInt(args, "pageSize"); };
        
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var filters = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (args.TryGetProperty("filters", out var f) && f.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in f.EnumerateObject())
            {
                // only accept string-like values; ignore null/others
                if (p.Value.ValueKind == JsonValueKind.String)
                    filters[p.Name] = p.Value.GetString();
                else if (p.Value.ValueKind == JsonValueKind.Number)
                    filters[p.Name] = p.Value.ToString();
                else if (p.Value.ValueKind == JsonValueKind.Null)
                    filters[p.Name] = null;
            }
        }

        return ValidationResult<ModelSearchDynamicIntent>.Success(
            new ModelSearchDynamicIntent(filters, page, pageSize));
    }
    public static ValidationResult<ModelsFiltersCatalogIntent> ValidateFiltersCatalog(JsonElement args)
    {
        // Tool không cần input
        return ValidationResult<ModelsFiltersCatalogIntent>.Success(new ModelsFiltersCatalogIntent());
    }


}
