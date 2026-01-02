using System.Text.Json;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

public static class OrdersSchemas
{
    public static ValidationResult<OrderQueryIntent> ValidateOrderQuery(JsonElement args)
    {
        var filters = SchemaParsing.ReadFilters(args);
        var page = Math.Max(1, SchemaParsing.ReadInt(args, "page", 1));
        var pageSize = Math.Clamp(SchemaParsing.ReadInt(args, "pageSize", 50), 1, 500);

        return ValidationResult<OrderQueryIntent>.Success(new OrderQueryIntent(filters, page, pageSize));
    }

    public static ValidationResult<OrderSummaryIntent> ValidateOrderSummary(JsonElement args)
    {
        var filters = SchemaParsing.ReadFilters(args);
        return ValidationResult<OrderSummaryIntent>.Success(new OrderSummaryIntent(filters));
    }

    public static ValidationResult<OrderCreatePrepareIntent> ValidateOrderCreatePrepare(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("customerId", out var customerElement) || customerElement.ValueKind != JsonValueKind.String || !Guid.TryParse(customerElement.GetString(), out var customerId))
        {
            return ValidationResult<OrderCreatePrepareIntent>.Fail("customerId is required and must be a GUID.");
        }

        if (!arguments.TryGetProperty("modelId", out var modelElement) || modelElement.ValueKind != JsonValueKind.String || !Guid.TryParse(modelElement.GetString(), out var modelId))
        {
            return ValidationResult<OrderCreatePrepareIntent>.Fail("modelId is required and must be a GUID.");
        }

        string? color = null;
        if (arguments.TryGetProperty("color", out var colorElement) && colorElement.ValueKind == JsonValueKind.String)
        {
            var text = colorElement.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                color = text.Trim();
            }
        }

        if (!arguments.TryGetProperty("quantity", out var qtyElement) || qtyElement.ValueKind != JsonValueKind.Number || !qtyElement.TryGetInt32(out var quantity) || quantity <= 0 || quantity > 1000)
        {
            return ValidationResult<OrderCreatePrepareIntent>.Fail("quantity is required and must be between 1 and 1000.");
        }

        return ValidationResult<OrderCreatePrepareIntent>.Success(new OrderCreatePrepareIntent(customerId, modelId, color, quantity));
    }

    public static ValidationResult<OrderCreateCommitIntent> ValidateOrderCreateCommit(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("confirmationId", out var idElement) || idElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            return ValidationResult<OrderCreateCommitIntent>.Fail("confirmationId is required.");
        }

        return ValidationResult<OrderCreateCommitIntent>.Success(new OrderCreateCommitIntent(idElement.GetString()!.Trim()));
    }
}
