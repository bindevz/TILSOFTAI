using System.Text.Json;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

public static class OrdersSchemas
{
    public static ValidationResult<OrderQueryIntent> ValidateOrderQuery(JsonElement arguments)
    {
        var customerId = GetGuid(arguments, "customerId");
        var status = GetStatus(arguments);
        var startDate = GetDate(arguments, "startDate");
        var endDate = GetDate(arguments, "endDate");
        var pageNumber = arguments.TryGetProperty("page", out var pageElement) && pageElement.ValueKind == JsonValueKind.Number
            ? pageElement.GetInt32()
            : 1;
        var pageSize = arguments.TryGetProperty("pageSize", out var sizeElement) && sizeElement.ValueKind == JsonValueKind.Number
            ? sizeElement.GetInt32()
            : 50;

        if (!startDate.HasValue && !endDate.HasValue)
        {
            return ValidationResult<OrderQueryIntent>.Fail("startDate or endDate is required.");
        }

        if (pageNumber <= 0 || pageSize <= 0)
        {
            return ValidationResult<OrderQueryIntent>.Fail("Pagination values must be positive.");
        }

        return ValidationResult<OrderQueryIntent>.Success(new OrderQueryIntent(customerId, status, startDate, endDate, pageNumber, pageSize));
    }

    public static ValidationResult<OrderSummaryIntent> ValidateOrderSummary(JsonElement arguments)
    {
        var customerId = GetGuid(arguments, "customerId");
        var status = GetStatus(arguments);
        var startDate = GetDate(arguments, "startDate");
        var endDate = GetDate(arguments, "endDate");

        if (!startDate.HasValue && !endDate.HasValue)
        {
            return ValidationResult<OrderSummaryIntent>.Fail("startDate or endDate is required.");
        }

        return ValidationResult<OrderSummaryIntent>.Success(new OrderSummaryIntent(customerId, status, startDate, endDate));
    }

    private static Guid? GetGuid(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static OrderStatus? GetStatus(JsonElement element)
    {
        if (element.TryGetProperty("status", out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (Enum.TryParse<OrderStatus>(text, true, out var status))
            {
                return status;
            }
        }

        return null;
    }
}
