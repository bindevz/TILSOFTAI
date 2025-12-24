using System.Text.Json;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

public static class CustomersSchemas
{
    public static ValidationResult<UpdateEmailIntent> ValidateUpdateEmail(JsonElement arguments)
    {
        if (arguments.TryGetProperty("confirmationId", out var confirmationElement) && confirmationElement.ValueKind == JsonValueKind.String)
        {
            var confirmationId = confirmationElement.GetString();
            if (string.IsNullOrWhiteSpace(confirmationId))
            {
                return ValidationResult<UpdateEmailIntent>.Fail("confirmationId is required.");
            }

            return ValidationResult<UpdateEmailIntent>.Success(new UpdateEmailIntent(null, null, confirmationId));
        }

        if (!arguments.TryGetProperty("customerId", out var idElement) || idElement.ValueKind != JsonValueKind.String || !Guid.TryParse(idElement.GetString(), out var customerId))
        {
            return ValidationResult<UpdateEmailIntent>.Fail("customerId is required and must be a GUID string.");
        }

        if (!arguments.TryGetProperty("email", out var emailElement) || emailElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(emailElement.GetString()))
        {
            return ValidationResult<UpdateEmailIntent>.Fail("email is required.");
        }

        var email = emailElement.GetString()!;
        return ValidationResult<UpdateEmailIntent>.Success(new UpdateEmailIntent(customerId, email, null));
    }

    public static ValidationResult<CustomerSearchIntent> ValidateSearch(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("query", out var queryElement) || queryElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(queryElement.GetString()))
        {
            return ValidationResult<CustomerSearchIntent>.Fail("query is required.");
        }

        if (!arguments.TryGetProperty("page", out var pageElement) || pageElement.ValueKind != JsonValueKind.Number || !pageElement.TryGetInt32(out var page) || page <= 0)
        {
            return ValidationResult<CustomerSearchIntent>.Fail("page is required and must be greater than 0.");
        }

        if (!arguments.TryGetProperty("pageSize", out var pageSizeElement) || pageSizeElement.ValueKind != JsonValueKind.Number || !pageSizeElement.TryGetInt32(out var pageSize) || pageSize <= 0 || pageSize > 50)
        {
            return ValidationResult<CustomerSearchIntent>.Fail("pageSize is required and must be between 1 and 50.");
        }

        var query = queryElement.GetString()!.Trim();
        return ValidationResult<CustomerSearchIntent>.Success(new CustomerSearchIntent(query, page, pageSize));
    }
}
