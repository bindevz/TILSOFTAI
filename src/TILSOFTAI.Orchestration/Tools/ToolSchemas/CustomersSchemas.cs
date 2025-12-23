using System.Text.Json;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

public static class CustomersSchemas
{
    public static ValidationResult<UpdateEmailIntent> ValidateUpdateEmail(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("customerId", out var idElement) || idElement.ValueKind != JsonValueKind.String || !Guid.TryParse(idElement.GetString(), out var customerId))
        {
            return ValidationResult<UpdateEmailIntent>.Fail("customerId is required and must be a GUID string.");
        }

        if (!arguments.TryGetProperty("email", out var emailElement) || emailElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(emailElement.GetString()))
        {
            return ValidationResult<UpdateEmailIntent>.Fail("email is required.");
        }

        var email = emailElement.GetString()!;
        return ValidationResult<UpdateEmailIntent>.Success(new UpdateEmailIntent(customerId, email));
    }
}
