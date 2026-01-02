using System.Text.Json;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

public static class CustomersSchemas
{
    public static ValidationResult<UpdateEmailIntent> ValidateUpdateEmail(JsonElement args)
    {
        // Commit path
        if (args.TryGetProperty("confirmationId", out var confirmationElement) && confirmationElement.ValueKind == JsonValueKind.String)
        {
            var confirmationId = confirmationElement.GetString();
            if (string.IsNullOrWhiteSpace(confirmationId))
                return ValidationResult<UpdateEmailIntent>.Fail("confirmationId is required.");

            return ValidationResult<UpdateEmailIntent>.Success(new UpdateEmailIntent(null, null, confirmationId.Trim()));
        }

        // Prepare path
        if (!args.TryGetProperty("customerId", out var idElement) || idElement.ValueKind != JsonValueKind.String || !Guid.TryParse(idElement.GetString(), out var customerId))
            return ValidationResult<UpdateEmailIntent>.Fail("customerId is required and must be a GUID string.");

        if (!args.TryGetProperty("email", out var emailElement) || emailElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(emailElement.GetString()))
            return ValidationResult<UpdateEmailIntent>.Fail("email is required.");

        return ValidationResult<UpdateEmailIntent>.Success(new UpdateEmailIntent(customerId, emailElement.GetString()!.Trim(), null));
    }

    public static ValidationResult<CustomerSearchIntent> ValidateSearch(JsonElement args)
    {
        var filters = SchemaParsing.ReadFilters(args);
        var page = Math.Max(1, SchemaParsing.ReadInt(args, "page", 1));
        var pageSize = Math.Clamp(SchemaParsing.ReadInt(args, "pageSize", 20), 1, 50);
        return ValidationResult<CustomerSearchIntent>.Success(new CustomerSearchIntent(filters, page, pageSize));
    }
}
