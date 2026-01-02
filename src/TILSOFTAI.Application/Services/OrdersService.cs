using System.Globalization;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Application.Validators;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Caching;

namespace TILSOFTAI.Application.Services;

public sealed class OrdersService
{
    private readonly IOrdersRepository _ordersRepository;
    private readonly ICustomersRepository _customersRepository;
    private readonly IModelRepository _modelRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly RbacService _rbac;
    private readonly AppMemoryCache _cache;
    private readonly ConfirmationPlanService _confirmationPlans;
    private readonly IAuditLogger _auditLogger;

    public OrdersService(IOrdersRepository ordersRepository, ICustomersRepository customersRepository, IModelRepository modelRepository, IUnitOfWork unitOfWork, RbacService rbac, AppMemoryCache cache, ConfirmationPlanService confirmationPlans, IAuditLogger auditLogger)
    {
        _ordersRepository = ordersRepository;
        _customersRepository = customersRepository;
        _modelRepository = modelRepository;
        _unitOfWork = unitOfWork;
        _rbac = rbac;
        _cache = cache;
        _confirmationPlans = confirmationPlans;
        _auditLogger = auditLogger;
    }

    public async Task<PagedResult<Order>> QueryOrdersAsync(OrderQuery query, TILSOFTAI.Domain.ValueObjects.TSExecutionContext context, CancellationToken cancellationToken)
    {
        _rbac.EnsureReadAllowed("orders.query", context);
        BusinessValidators.ValidateOrderQuery(query);

        return await _ordersRepository.QueryAsync(context.TenantId, query, cancellationToken);
    }

    public async Task<OrderSummary> SummarizeOrdersAsync(OrderQuery query, TILSOFTAI.Domain.ValueObjects.TSExecutionContext context, CancellationToken cancellationToken)
    {
        _rbac.EnsureReadAllowed("orders.summary", context);
        BusinessValidators.ValidateOrderQuery(query);

        var cacheKey = $"orders:summary:{context.TenantId}:{query.CustomerId}:{query.Status}:{query.StartDate}:{query.EndDate}";
        return await _cache.GetOrAddAsync(cacheKey,
            () => _ordersRepository.SummarizeAsync(context.TenantId, query, cancellationToken),
            TimeSpan.FromMinutes(1));
    }

    public async Task<object> PrepareCreateAsync(Guid customerId, Guid modelId, string? color, int quantity, TILSOFTAI.Domain.ValueObjects.TSExecutionContext context, CancellationToken ct)
    {
        _rbac.EnsureWriteAllowed("orders.create.prepare", context);
        BusinessValidators.EnsureWriteAuthorized("orders.create.prepare", context, new[] { "admin", "ops" });

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        var customer = await _customersRepository.GetByIdAsync(context.TenantId, customerId, ct) ?? throw new KeyNotFoundException("Customer not found.");
        BusinessValidators.EnsureCustomerIsActive(customer);

        var model = await _modelRepository.GetAsync(context.TenantId, modelId, ct) ?? throw new KeyNotFoundException("Model not found.");
        var total = model.BasePrice * quantity;
        var colorValue = string.IsNullOrWhiteSpace(color) ? "N/A" : color.Trim();
        var reference = $"MODEL={modelId};COLOR={colorValue};QTY={quantity}";

        var plan = await _confirmationPlans.CreatePlanAsync(
            "orders.create",
            context,
            new Dictionary<string, string>
            {
                ["customerId"] = customerId.ToString(),
                ["modelId"] = modelId.ToString(),
                ["color"] = colorValue,
                ["quantity"] = quantity.ToString(CultureInfo.InvariantCulture),
                ["total"] = total.ToString(CultureInfo.InvariantCulture),
                ["currency"] = "USD",
                ["reference"] = reference
            },
            ct);

        return new
        {
            confirmation_id = plan.Id,
            expires_at = plan.ExpiresAt,
            preview = new
            {
                customerId,
                modelId,
                color = colorValue,
                quantity,
                totalAmount = total,
                currency = "USD",
                reference
            }
        };
    }

    public async Task<Order> CommitCreateAsync(string confirmationId, TILSOFTAI.Domain.ValueObjects.TSExecutionContext context, CancellationToken ct)
    {
        _rbac.EnsureWriteAllowed("orders.create.commit", context);
        BusinessValidators.EnsureWriteAuthorized("orders.create.commit", context, new[] { "admin", "ops" });

        var plan = await _confirmationPlans.ConsumePlanAsync(confirmationId, "orders.create", context, ct);

        if (!plan.Data.TryGetValue("customerId", out var customerText) || !Guid.TryParse(customerText, out var customerId))
        {
            throw new InvalidOperationException("Invalid confirmation payload.");
        }

        if (!plan.Data.TryGetValue("modelId", out var modelText) || !Guid.TryParse(modelText, out var modelId))
        {
            throw new InvalidOperationException("Invalid confirmation payload.");
        }

        if (!plan.Data.TryGetValue("quantity", out var qtyText) || !int.TryParse(qtyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity))
        {
            throw new InvalidOperationException("Invalid confirmation payload.");
        }

        if (!plan.Data.TryGetValue("total", out var totalText) || !decimal.TryParse(totalText, NumberStyles.Number, CultureInfo.InvariantCulture, out var total))
        {
            throw new InvalidOperationException("Invalid confirmation payload.");
        }

        var currency = plan.Data.TryGetValue("currency", out var currencyValue) && !string.IsNullOrWhiteSpace(currencyValue)
            ? currencyValue
            : "USD";

        var reference = plan.Data.TryGetValue("reference", out var referenceValue) ? referenceValue : string.Empty;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            CustomerId = customerId,
            OrderDate = DateTimeOffset.UtcNow,
            Status = OrderStatus.Pending,
            TotalAmount = total,
            Currency = currency,
            Reference = reference,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _unitOfWork.ExecuteTransactionalAsync(async token =>
        {
            await _ordersRepository.CreateAsync(order, token);
        }, ct);

        await _auditLogger.LogToolExecutionAsync(context, "orders.create.commit", new { confirmationId }, new { order.Id, order.CustomerId, order.TotalAmount, order.Reference }, ct);

        return order;
    }
}
