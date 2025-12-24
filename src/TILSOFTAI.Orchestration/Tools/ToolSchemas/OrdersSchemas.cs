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
        var startDate = RequireDate(arguments, "startDate");
        var endDate = RequireDate(arguments, "endDate");
        var season = GetString(arguments, "season");
        var metric = GetString(arguments, "metric");
        var pageNumber = RequireInt(arguments, "page");
        var pageSize = RequireInt(arguments, "pageSize");

        if (!startDate.HasValue && !endDate.HasValue)
        {
            return ValidationResult<OrderQueryIntent>.Fail("startDate or endDate is required.");
        }

        if (pageNumber <= 0 || pageSize <= 0)
        {
            return ValidationResult<OrderQueryIntent>.Fail("Pagination values must be positive.");
        }

        return ValidationResult<OrderQueryIntent>.Success(new OrderQueryIntent(customerId, status, startDate, endDate, pageNumber, pageSize, season, metric));
    }

    public static ValidationResult<OrderSummaryIntent> ValidateOrderSummary(JsonElement arguments)
    {
        var customerId = GetGuid(arguments, "customerId");
        var status = GetStatus(arguments);
        var startDate = RequireDate(arguments, "startDate");
        var endDate = RequireDate(arguments, "endDate");
        var season = GetString(arguments, "season");
        var metric = GetString(arguments, "metric");

        if (!startDate.HasValue && !endDate.HasValue)
        {
            return ValidationResult<OrderSummaryIntent>.Fail("startDate or endDate is required.");
        }

        return ValidationResult<OrderSummaryIntent>.Success(new OrderSummaryIntent(customerId, status, startDate, endDate, season, metric));
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

    private static string? GetString(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static DateTimeOffset? RequireDate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        throw new ArgumentException($"{property} must be an ISO-8601 string date.");
    }

    private static int RequireInt(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed))
        {
            throw new ArgumentException($"{property} is required and must be an integer.");
        }

        return parsed;
    }
}
