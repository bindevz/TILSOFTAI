using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Orchestration.Tools;

public sealed class ToolDispatcher
{
    private readonly OrdersService _ordersService;
    private readonly CustomersService _customersService;

    public ToolDispatcher(OrdersService ordersService, CustomersService customersService)
    {
        _ordersService = ordersService;
        _customersService = customersService;
    }

    public async Task<ToolExecutionResult?> DispatchAsync(string toolName, object intent, TILSOFTAI.Domain.ValueObjects.ExecutionContext context, CancellationToken cancellationToken)
    {
        return toolName switch
        {
            "orders.query" => await HandleOrdersQueryAsync((OrderQueryIntent)intent, context, cancellationToken),
            "orders.summary" => await HandleOrdersSummaryAsync((OrderSummaryIntent)intent, context, cancellationToken),
            "customers.updateEmail" => await HandleUpdateEmailAsync((UpdateEmailIntent)intent, context, cancellationToken),
            _ => null
        };
    }

    private async Task<ToolExecutionResult> HandleOrdersQueryAsync(OrderQueryIntent intent, TILSOFTAI.Domain.ValueObjects.ExecutionContext context, CancellationToken cancellationToken)
    {
        var endDate = intent.EndDate ?? DateTimeOffset.UtcNow;
        var startDate = intent.StartDate ?? endDate.AddDays(-90);

        var query = new OrderQuery
        {
            CustomerId = intent.CustomerId,
            Status = intent.Status,
            StartDate = startDate,
            EndDate = endDate,
            PageNumber = intent.PageNumber,
            PageSize = intent.PageSize
        };

        var result = await _ordersService.QueryOrdersAsync(query, context, cancellationToken);
        var payload = new
        {
            result.TotalCount,
            result.PageNumber,
            result.PageSize,
            Orders = result.Items.Select(o => new
            {
                o.Id,
                o.CustomerId,
                o.OrderDate,
                o.Status,
                o.TotalAmount,
                o.Currency,
                o.Reference
            }).ToArray()
        };

        return ToolExecutionResult.CreateSuccess("orders.query executed", payload);
    }

    private async Task<ToolExecutionResult> HandleOrdersSummaryAsync(OrderSummaryIntent intent, TILSOFTAI.Domain.ValueObjects.ExecutionContext context, CancellationToken cancellationToken)
    {
        var endDate = intent.EndDate ?? DateTimeOffset.UtcNow;
        var startDate = intent.StartDate ?? endDate.AddDays(-90);

        var query = new OrderQuery
        {
            CustomerId = intent.CustomerId,
            Status = intent.Status,
            StartDate = startDate,
            EndDate = endDate,
            PageNumber = 1,
            PageSize = 1
        };

        var summary = await _ordersService.SummarizeOrdersAsync(query, context, cancellationToken);
        var payload = new
        {
            summary.Count,
            summary.TotalAmount,
            summary.AverageAmount,
            CountByStatus = summary.CountByStatus.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value)
        };

        return ToolExecutionResult.CreateSuccess("orders.summary executed", payload);
    }

    private async Task<ToolExecutionResult> HandleUpdateEmailAsync(UpdateEmailIntent intent, TILSOFTAI.Domain.ValueObjects.ExecutionContext context, CancellationToken cancellationToken)
    {
        var updated = await _customersService.UpdateEmailAsync(intent.CustomerId, intent.Email, context, cancellationToken);
        var payload = new
        {
            updated.Id,
            updated.Email,
            updated.Name,
            updated.IsActive,
            updated.UpdatedAt
        };

        return ToolExecutionResult.CreateSuccess("customers.updateEmail executed", payload);
    }
}
