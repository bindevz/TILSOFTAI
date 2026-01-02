using System.Text.Json;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

public static class FiltersSchemas
{
    public static ValidationResult<FiltersCatalogIntent> ValidateCatalog(JsonElement args)
    {
        string? resource = null;
        if (args.TryGetProperty("resource", out var r) && r.ValueKind == JsonValueKind.String)
            resource = r.GetString();

        var includeValues = false;
        if (args.TryGetProperty("includeValues", out var iv))
        {
            if (iv.ValueKind == JsonValueKind.True) includeValues = true;
            if (iv.ValueKind == JsonValueKind.False) includeValues = false;
        }

        // resource optional: null => list resources
        return ValidationResult<FiltersCatalogIntent>.Success(new FiltersCatalogIntent(resource, includeValues));
    }
}
