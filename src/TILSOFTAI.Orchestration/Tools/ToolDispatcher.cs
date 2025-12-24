using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Chat;
using TILSOFTAI.Orchestration.Llm;
using ExecutionContext = TILSOFTAI.Domain.ValueObjects.ExecutionContext;

namespace TILSOFTAI.Orchestration.Tools;

public sealed class ToolDispatcher
{
    private readonly OrdersService _ordersService;
    private readonly CustomersService _customersService;
    private readonly ModelsService _modelsService;
    private readonly RbacService _rbacService;
    private readonly SemanticResolver _semanticResolver;

    public ToolDispatcher(OrdersService ordersService, CustomersService customersService, ModelsService modelsService, RbacService rbacService, SemanticResolver semanticResolver)
    {
        _ordersService = ordersService;
        _customersService = customersService;
        _modelsService = modelsService;
        _rbacService = rbacService;
        _semanticResolver = semanticResolver;
    }

    public async Task<ToolDispatchResult> DispatchAsync(string toolName, object intent, ExecutionContext context, bool requiresWrite, CancellationToken cancellationToken)
    {
        if (requiresWrite)
        {
            _rbacService.EnsureWriteAllowed(toolName, context);
        }
        else
        {
            _rbacService.EnsureReadAllowed(toolName, context);
        }

        return toolName switch
        {
            "orders.query" => await HandleOrdersQueryAsync((OrderQueryIntent)intent, context, cancellationToken),
            "orders.summary" => await HandleOrdersSummaryAsync((OrderSummaryIntent)intent, context, cancellationToken),
            "customers.updateEmail" => await HandleUpdateEmailAsync((UpdateEmailIntent)intent, context, cancellationToken),
            "customers.search" => await HandleCustomersSearchAsync((CustomerSearchIntent)intent, context, cancellationToken),
            "models.search" => await HandleModelsSearchAsync((ModelSearchIntent)intent, context, cancellationToken),
            "models.get" => await HandleModelGetAsync((ModelGetIntent)intent, context, cancellationToken),
            "models.attributes.list" => await HandleModelAttributesAsync((ModelListAttributesIntent)intent, context, cancellationToken),
            "models.price.analyze" => await HandleModelPriceAnalyzeAsync((ModelPriceAnalyzeIntent)intent, context, cancellationToken),
            "models.create.prepare" => await HandleModelCreatePrepareAsync((ModelCreatePrepareIntent)intent, context, cancellationToken),
            "models.create.commit" => await HandleModelCreateCommitAsync((ModelCreateCommitIntent)intent, context, cancellationToken),
            "orders.create.prepare" => await HandleOrdersCreatePrepareAsync((OrderCreatePrepareIntent)intent, context, cancellationToken),
            "orders.create.commit" => await HandleOrdersCreateCommitAsync((OrderCreateCommitIntent)intent, context, cancellationToken),
            _ => throw new ResponseContractException("Tool not allowed.")
        };
    }

    private async Task<ToolDispatchResult> HandleOrdersQueryAsync(OrderQueryIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var normalized = NormalizeOrderQuery(intent);

        var query = new OrderQuery
        {
            CustomerId = normalized.CustomerId,
            Status = normalized.Status,
            StartDate = normalized.StartDate,
            EndDate = normalized.EndDate,
            PageNumber = normalized.PageNumber,
            PageSize = normalized.PageSize
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

        return CreateResult(normalized, ToolExecutionResult.CreateSuccess("orders.query executed", payload));
    }

    private async Task<ToolDispatchResult> HandleOrdersSummaryAsync(OrderSummaryIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var normalized = NormalizeOrderSummary(intent);

        var query = new OrderQuery
        {
            CustomerId = normalized.CustomerId,
            Status = normalized.Status,
            StartDate = normalized.StartDate,
            EndDate = normalized.EndDate,
            PageNumber = 1,
            PageSize = 1
        };

        var summary = await _ordersService.SummarizeOrdersAsync(query, context, cancellationToken);
        var payload = new
        {
            summary.Count,
            summary.TotalAmount,
            summary.AverageAmount,
            summary.MinAmount,
            summary.MaxAmount,
            CountByStatus = summary.CountByStatus.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value),
            TopCustomers = summary.TopCustomers.Select(x => new { x.CustomerId, x.CustomerName, x.TotalAmount, x.OrderCount })
        };

        return CreateResult(normalized, ToolExecutionResult.CreateSuccess("orders.summary executed", payload));
    }

    private async Task<ToolDispatchResult> HandleUpdateEmailAsync(UpdateEmailIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(intent.ConfirmationId))
        {
            var committed = await _customersService.CommitUpdateEmailAsync(intent.ConfirmationId!, context, cancellationToken);
            var payload = new
            {
                committed.Id,
                committed.Email,
                committed.Name,
                committed.IsActive,
                committed.UpdatedAt
            };

            return CreateResult(intent, ToolExecutionResult.CreateSuccess("customers.updateEmail committed", payload));
        }

        var prepare = await _customersService.PrepareUpdateEmailAsync(intent.CustomerId!.Value, intent.Email!, context, cancellationToken);
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("customers.updateEmail prepared", prepare));
    }

    private async Task<ToolDispatchResult> HandleModelsSearchAsync(ModelSearchIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await _modelsService.SearchAsync(intent.Category, intent.Name, intent.Page, intent.PageSize, context, cancellationToken);
        var payload = new
        {
            result.TotalCount,
            result.PageNumber,
            result.PageSize,
            Models = result.Items.Select(m => new
            {
                m.Id,
                m.Name,
                m.Category,
                m.BasePrice
            })
        };
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.search executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelGetAsync(ModelGetIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var model = await _modelsService.GetAsync(intent.ModelId, context, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");

        var payload = new
        {
            model.Id,
            model.Name,
            model.Category,
            model.BasePrice,
            Attributes = model.Attributes.Select(a => new { a.Name, a.Value })
        };
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.get executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelAttributesAsync(ModelListAttributesIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var attributes = await _modelsService.ListAttributesAsync(intent.ModelId, context, cancellationToken);
        var payload = attributes.Select(a => new { a.Name, a.Value });
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.attributes.list executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelPriceAnalyzeAsync(ModelPriceAnalyzeIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var analysis = await _modelsService.AnalyzePriceAsync(intent.ModelId, context, cancellationToken);
        var payload = new
        {
            analysis.BasePrice,
            analysis.AttributeAdjustment,
            analysis.FinalPrice
        };
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.price.analyze executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelCreatePrepareAsync(ModelCreatePrepareIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var prepare = await _modelsService.PrepareCreateAsync(intent.Name, intent.Category, intent.BasePrice, intent.Attributes, context, cancellationToken);
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.create.prepare executed", prepare));
    }

    private async Task<ToolDispatchResult> HandleModelCreateCommitAsync(ModelCreateCommitIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var created = await _modelsService.CommitCreateAsync(intent.ConfirmationId, context, cancellationToken);
        var payload = new
        {
            created.Id,
            created.Name,
            created.Category,
            created.BasePrice
        };
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.create.commit executed", payload));
    }

    private async Task<ToolDispatchResult> HandleCustomersSearchAsync(CustomerSearchIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await _customersService.SearchAsync(intent.Query, intent.Page, intent.PageSize, context, cancellationToken);
        var payload = new
        {
            result.TotalCount,
            result.PageNumber,
            result.PageSize,
            Customers = result.Items.Select(c => new { c.Id, c.Name, c.Email, c.IsActive })
        };

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("customers.search executed", payload));
    }

    private async Task<ToolDispatchResult> HandleOrdersCreatePrepareAsync(OrderCreatePrepareIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await _ordersService.PrepareCreateAsync(intent.CustomerId, intent.ModelId, intent.Color, intent.Quantity, context, cancellationToken);
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("orders.create.prepare executed", result));
    }

    private async Task<ToolDispatchResult> HandleOrdersCreateCommitAsync(OrderCreateCommitIntent intent, ExecutionContext context, CancellationToken cancellationToken)
    {
        var order = await _ordersService.CommitCreateAsync(intent.ConfirmationId, context, cancellationToken);
        var payload = new
        {
            order.Id,
            order.CustomerId,
            order.OrderDate,
            order.Status,
            order.TotalAmount,
            order.Currency,
            order.Reference
        };
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("orders.create.commit executed", payload));
    }

    private OrderQueryIntent NormalizeOrderQuery(OrderQueryIntent intent)
    {
        var endDate = intent.EndDate ?? DateTimeOffset.UtcNow;
        var startDate = intent.StartDate ?? endDate.AddDays(-90);
        var normalizedSeason = string.IsNullOrWhiteSpace(intent.Season) ? null : _semanticResolver.NormalizeSeason(intent.Season).Value;
        var normalizedMetric = string.IsNullOrWhiteSpace(intent.Metric) ? null : _semanticResolver.NormalizeMetric(intent.Metric).Value;

        var normalized = intent with
        {
            StartDate = startDate,
            EndDate = endDate,
            Season = normalizedSeason,
            Metric = normalizedMetric
        };

        if (IsQueryOverlyBroad(normalized))
        {
            throw new ResponseContractException("Query too broad.");
        }

        return normalized;
    }

    private OrderSummaryIntent NormalizeOrderSummary(OrderSummaryIntent intent)
    {
        var endDate = intent.EndDate ?? DateTimeOffset.UtcNow;
        var startDate = intent.StartDate ?? endDate.AddDays(-90);
        var normalizedSeason = string.IsNullOrWhiteSpace(intent.Season) ? null : _semanticResolver.NormalizeSeason(intent.Season).Value;
        var normalizedMetric = string.IsNullOrWhiteSpace(intent.Metric) ? null : _semanticResolver.NormalizeMetric(intent.Metric).Value;

        return intent with
        {
            StartDate = startDate,
            EndDate = endDate,
            Season = normalizedSeason,
            Metric = normalizedMetric
        };
    }

    private static bool IsQueryOverlyBroad(OrderQueryIntent intent)
    {
        if (!intent.StartDate.HasValue && !intent.EndDate.HasValue)
        {
            return true;
        }

        var upper = intent.EndDate ?? DateTimeOffset.UtcNow;
        var lower = intent.StartDate ?? upper.AddDays(-90);
        var days = (upper - lower).TotalDays;

        return intent.PageSize > 500 || days > 365;
    }

    private static ToolDispatchResult CreateResult(object normalizedIntent, ToolExecutionResult result) =>
        new(normalizedIntent, result);
}

public sealed record ToolDispatchResult(object NormalizedIntent, ToolExecutionResult Result);
