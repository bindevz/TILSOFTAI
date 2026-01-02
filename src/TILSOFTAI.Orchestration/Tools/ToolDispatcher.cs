using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Chat;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Tools.FiltersCatalog;
using TILSOFTAI.Orchestration.Tools.Filters;

namespace TILSOFTAI.Orchestration.Tools;

public sealed class ToolDispatcher
{
    private readonly OrdersService _ordersService;
    private readonly CustomersService _customersService;
    private readonly ModelsService _modelsService;
    private readonly IFilterCatalogService _filterCatalogService;
    private readonly SemanticResolver _semanticResolver;

    public ToolDispatcher(
        OrdersService ordersService,
        CustomersService customersService,
        ModelsService modelsService,
        IFilterCatalogService filterCatalogService,
        SemanticResolver semanticResolver)
    {
        _ordersService = ordersService;
        _customersService = customersService;
        _modelsService = modelsService;
        _filterCatalogService = filterCatalogService;
        _semanticResolver = semanticResolver;
    }

    public async Task<ToolDispatchResult> DispatchAsync(string toolName, object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        return toolName.ToLowerInvariant() switch
        {
            "models.search" => await HandleModelsSearchAsync((ModelsSearchIntent)intent, context, cancellationToken),
            "models.count" => await HandleModelsCountAsync((ModelsCountIntent)intent, context, cancellationToken),
            "models.get" => await HandleModelsGetAsync((ModelGetIntent)intent, context, cancellationToken),
            "models.attributes.list" => await HandleModelsAttributesAsync((ModelListAttributesIntent)intent, context, cancellationToken),
            "models.price.analyze" => await HandleModelsPriceAsync((ModelPriceAnalyzeIntent)intent, context, cancellationToken),
            "models.create.prepare" => await HandleModelsCreatePrepareAsync((ModelCreatePrepareIntent)intent, context, cancellationToken),
            "models.create.commit" => await HandleModelsCreateCommitAsync((ModelCreateCommitIntent)intent, context, cancellationToken),

            "customers.search" => await HandleCustomersSearchAsync((CustomerSearchIntent)intent, context, cancellationToken),
            "customers.updateemail" => await HandleUpdateEmailAsync((UpdateEmailIntent)intent, context, cancellationToken),

            "orders.query" => await HandleOrdersQueryAsync((OrderQueryIntent)intent, context, cancellationToken),
            "orders.summary" => await HandleOrdersSummaryAsync((OrderSummaryIntent)intent, context, cancellationToken),
            "orders.create.prepare" => await HandleOrdersCreatePrepareAsync((OrderCreatePrepareIntent)intent, context, cancellationToken),
            "orders.create.commit" => await HandleOrdersCreateCommitAsync((OrderCreateCommitIntent)intent, context, cancellationToken),

            "filters.catalog" => await HandleFiltersCatalogAsync((FiltersCatalogIntent)intent, context, cancellationToken),

            _ => throw new ResponseContractException("Tool not supported.")
        };
    }

    private async Task<ToolDispatchResult> HandleModelsSearchAsync(ModelsSearchIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var (filtersApplied, rejected) = CanonicalizeFilters("models.search", intent.Filters);
        if (filtersApplied.TryGetValue("season", out var seasonRaw))
        {
            filtersApplied["season"] = SeasonNormalizer.NormalizeToFull(seasonRaw);
        }

        var result = await _modelsService.SearchAsync(
            context.TenantId,
            filtersApplied.GetValueOrDefault("rangeName"),
            filtersApplied.GetValueOrDefault("modelCode"),
            filtersApplied.GetValueOrDefault("modelName"),
            filtersApplied.GetValueOrDefault("season"),
            filtersApplied.GetValueOrDefault("collection"),
            intent.Page,
            intent.PageSize,
            context,
            cancellationToken);

        var payload = new
        {
            result.TotalCount,
            result.PageNumber,
            result.PageSize,
            filtersApplied,
            rejectedFilters = rejected,
            Models = result.Items.Select(m => new { m.ModelID, m.ModelUD, m.ModelNM, m.Season, m.Collection, m.RangeName })
        };

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.search executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelsCountAsync(ModelsCountIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var (filtersApplied, rejected) = CanonicalizeFilters("models.search", intent.Filters);
        if (filtersApplied.TryGetValue("season", out var seasonRaw))
        {
            filtersApplied["season"] = SeasonNormalizer.NormalizeToFull(seasonRaw);
        }

        // Reuse the search stored procedure: request 1 row, read TotalCount.
        var result = await _modelsService.SearchAsync(
            context.TenantId,
            filtersApplied.GetValueOrDefault("rangeName"),
            filtersApplied.GetValueOrDefault("modelCode"),
            filtersApplied.GetValueOrDefault("modelName"),
            filtersApplied.GetValueOrDefault("season"),
            filtersApplied.GetValueOrDefault("collection"),
            page: 1,
            size: 1,
            context: context,
            cancellationToken: cancellationToken);

        var payload = new
        {
            totalCount = result.TotalCount,
            filtersApplied,
            rejectedFilters = rejected
        };

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.count executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelsGetAsync(ModelGetIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var model = await _modelsService.GetAsync(intent.ModelId, context, cancellationToken);
        var payload = new
        {
            model.ModelID,
            model.ModelUD,
            model.ModelNM,
            model.Season,
            model.Collection,
            model.RangeName
        };

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.get executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelsAttributesAsync(ModelListAttributesIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var attrs = await _modelsService.ListAttributesAsync(intent.ModelId, context, cancellationToken);
        var payload = new
        {
            modelId = intent.ModelId,
            attributes = attrs.Select(a => new { a.Name, a.Value })
        };
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.attributes.list executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelsPriceAsync(ModelPriceAnalyzeIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var analysis = await _modelsService.AnalyzePriceAsync(intent.ModelId, context, cancellationToken);
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.price.analyze executed", analysis));
    }

    private async Task<ToolDispatchResult> HandleModelsCreatePrepareAsync(ModelCreatePrepareIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await _modelsService.PrepareCreateAsync(intent.Name, intent.Category, intent.BasePrice, intent.Attributes, context, cancellationToken);
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.create.prepare executed", result));
    }

    private async Task<ToolDispatchResult> HandleModelsCreateCommitAsync(ModelCreateCommitIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var created = await _modelsService.CommitCreateAsync(intent.ConfirmationId, context, cancellationToken);
        var payload = new
        {
            created.ModelID,
            created.ModelUD,
            created.ModelNM
        };
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.create.commit executed", payload));
    }

    private async Task<ToolDispatchResult> HandleUpdateEmailAsync(UpdateEmailIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        //if (!string.IsNullOrWhiteSpace(intent.ConfirmationId))
        //{
        //    var committed = await _customersService.CommitEmailUpdateAsync(intent.ConfirmationId, context, cancellationToken);
        //    return CreateResult(intent, ToolExecutionResult.CreateSuccess("customers.updateEmail executed", committed));
        //}

        //if (!intent.CustomerId.HasValue || string.IsNullOrWhiteSpace(intent.Email))
        //{
        //    throw new ResponseContractException("customerId and email are required.");
        //}

        //var prepared = await _customersService.PrepareEmailUpdateAsync(intent.CustomerId.Value, intent.Email, context, cancellationToken);
        //return CreateResult(intent, ToolExecutionResult.CreateSuccess("customers.updateEmail executed", prepared));
        throw new ResponseContractException("customerId and email are required.");
    }

    private async Task<ToolDispatchResult> HandleCustomersSearchAsync(CustomerSearchIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var (filtersApplied, rejected) = CanonicalizeFilters("customers.search", intent.Filters);
        var query = filtersApplied.GetValueOrDefault("query");
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ResponseContractException("customers.search requires filters.query (tên/email/mã).");
        }

        var result = await _customersService.SearchAsync(query, intent.Page, intent.PageSize, context, cancellationToken);
        var payload = new
        {
            result.TotalCount,
            result.PageNumber,
            result.PageSize,
            filtersApplied,
            rejectedFilters = rejected,
            Customers = result.Items.Select(c => new { c.Id, c.Name, c.Email, c.IsActive })
        };

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("customers.search executed", payload));
    }

    private async Task<ToolDispatchResult> HandleOrdersQueryAsync(OrderQueryIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var (filtersApplied, rejected) = CanonicalizeFilters("orders.query", intent.Filters);

        var customerId = ParseGuidOrNull(filtersApplied.GetValueOrDefault("customerId"));
        var status = ParseOrderStatusOrNull(filtersApplied.GetValueOrDefault("status"));
        var startDate = ParseDateOrNull(filtersApplied.GetValueOrDefault("startDate"));
        var endDate = ParseDateOrNull(filtersApplied.GetValueOrDefault("endDate"));

        var season = filtersApplied.GetValueOrDefault("season");
        if (!string.IsNullOrWhiteSpace(season))
            season = _semanticResolver.NormalizeSeason(season).Value;

        var metric = filtersApplied.GetValueOrDefault("metric");
        if (!string.IsNullOrWhiteSpace(metric))
            metric = _semanticResolver.NormalizeMetric(metric).Value;

        // Default date window (last 90 days) for remindable analytics.
        var normalizedEnd = endDate ?? DateTimeOffset.UtcNow;
        var normalizedStart = startDate ?? normalizedEnd.AddDays(-90);
        if ((normalizedEnd - normalizedStart).TotalDays > 365)
            throw new ResponseContractException("Date range too large (max 365 days).");
        if (intent.PageSize > 500)
            throw new ResponseContractException("pageSize too large (max 500).");

        var query = new OrderQuery
        {
            CustomerId = customerId,
            Status = status,
            StartDate = normalizedStart,
            EndDate = normalizedEnd,
            PageNumber = intent.PageNumber,
            PageSize = intent.PageSize
        };

        var result = await _ordersService.QueryOrdersAsync(query, context, cancellationToken);
        var payload = new
        {
            result.TotalCount,
            result.PageNumber,
            result.PageSize,
            filtersApplied = new Dictionary<string, string?>(filtersApplied, StringComparer.OrdinalIgnoreCase)
            {
                ["startDate"] = normalizedStart.ToString("O"),
                ["endDate"] = normalizedEnd.ToString("O"),
                ["season"] = season,
                ["metric"] = metric
            },
            rejectedFilters = rejected,
            Orders = result.Items.Select(o => new { o.Id, o.CustomerId, o.OrderDate, o.Status, o.TotalAmount, o.Currency, o.Reference })
        };

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("orders.query executed", payload));
    }

    private async Task<ToolDispatchResult> HandleOrdersSummaryAsync(OrderSummaryIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var (filtersApplied, rejected) = CanonicalizeFilters("orders.summary", intent.Filters);
        var customerId = ParseGuidOrNull(filtersApplied.GetValueOrDefault("customerId"));
        var status = ParseOrderStatusOrNull(filtersApplied.GetValueOrDefault("status"));
        var startDate = ParseDateOrNull(filtersApplied.GetValueOrDefault("startDate"));
        var endDate = ParseDateOrNull(filtersApplied.GetValueOrDefault("endDate"));

        var normalizedEnd = endDate ?? DateTimeOffset.UtcNow;
        var normalizedStart = startDate ?? normalizedEnd.AddDays(-90);
        if ((normalizedEnd - normalizedStart).TotalDays > 365)
            throw new ResponseContractException("Date range too large (max 365 days).");

        var query = new OrderQuery
        {
            CustomerId = customerId,
            Status = status,
            StartDate = normalizedStart,
            EndDate = normalizedEnd,
            PageNumber = 1,
            PageSize = 1
        };

        var summary = await _ordersService.SummarizeOrdersAsync(query, context, cancellationToken);

        var payload = new
        {
            filtersApplied = new Dictionary<string, string?>(filtersApplied, StringComparer.OrdinalIgnoreCase)
            {
                ["startDate"] = normalizedStart.ToString("O"),
                ["endDate"] = normalizedEnd.ToString("O")
            },
            rejectedFilters = rejected,
            summary
        };

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("orders.summary executed", payload));
    }

    private async Task<ToolDispatchResult> HandleOrdersCreatePrepareAsync(OrderCreatePrepareIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await _ordersService.PrepareCreateAsync(intent.CustomerId, intent.ModelId, intent.Color, intent.Quantity, context, cancellationToken);
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("orders.create.prepare executed", result));
    }

    private async Task<ToolDispatchResult> HandleOrdersCreateCommitAsync(OrderCreateCommitIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
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

    private async Task<ToolDispatchResult> HandleFiltersCatalogAsync(FiltersCatalogIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var payload = await _filterCatalogService.GetCatalogAsync(context, intent.Resource, intent.IncludeValues, cancellationToken);
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("filters.catalog executed", payload));
    }

    private static (Dictionary<string, string?> FiltersApplied, string[] Rejected) CanonicalizeFilters(string resource, IReadOnlyDictionary<string, string?> incoming)
    {
        if (!FilterCatalogRegistry.TryGet(resource, out var catalog))
            return (new Dictionary<string, string?>(incoming, StringComparer.OrdinalIgnoreCase), Array.Empty<string>());

        var aliasToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in catalog.SupportedFilters)
        {
            aliasToKey[f.Key] = f.Key;
            foreach (var a in f.Aliases ?? Array.Empty<string>())
                aliasToKey[a] = f.Key;
        }

        var applied = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var rejected = new List<string>();

        foreach (var (k, v) in incoming)
        {
            if (string.IsNullOrWhiteSpace(k))
                continue;

            if (!aliasToKey.TryGetValue(k.Trim(), out var canonicalKey))
            {
                rejected.Add(k);
                continue;
            }

            applied[canonicalKey] = v?.Trim();
        }

        return (applied, rejected.ToArray());
    }

    private static Guid? ParseGuidOrNull(string? value)
        => Guid.TryParse(value, out var id) ? id : null;

    private static DateTimeOffset? ParseDateOrNull(string? value)
        => DateTimeOffset.TryParse(value, out var dt) ? dt : null;

    private static OrderStatus? ParseOrderStatusOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        // allow numeric
        if (int.TryParse(value, out var i) && Enum.IsDefined(typeof(OrderStatus), i))
            return (OrderStatus)i;

        return Enum.TryParse<OrderStatus>(value, ignoreCase: true, out var status) ? status : null;
    }

    private static ToolDispatchResult CreateResult(object normalizedIntent, ToolExecutionResult result) =>
        new(normalizedIntent, result);
}

public sealed record ToolDispatchResult(object NormalizedIntent, ToolExecutionResult Result);
