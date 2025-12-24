using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Application.Validators;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using ExecutionContext = TILSOFTAI.Domain.ValueObjects.ExecutionContext;

namespace TILSOFTAI.Application.Services;

public sealed class CustomersService
{
    private readonly ICustomersRepository _customersRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly RbacService _rbac;
    private readonly IAuditLogger _auditLogger;
    private readonly ConfirmationPlanService _confirmationPlanService;

    public CustomersService(ICustomersRepository customersRepository, IUnitOfWork unitOfWork, RbacService rbac, IAuditLogger auditLogger, ConfirmationPlanService confirmationPlanService)
    {
        _customersRepository = customersRepository;
        _unitOfWork = unitOfWork;
        _rbac = rbac;
        _auditLogger = auditLogger;
        _confirmationPlanService = confirmationPlanService;
    }

    public async Task<PagedResult<Customer>> SearchAsync(string query, int page, int pageSize, ExecutionContext context, CancellationToken cancellationToken)
    {
        _rbac.EnsureReadAllowed("customers.search", context);
        BusinessValidators.ValidatePage(page, pageSize, maxSize: 50);

        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query is required.", nameof(query));
        }

        return await _customersRepository.SearchAsync(context.TenantId, query, page, pageSize, cancellationToken);
    }

    public async Task<object> PrepareUpdateEmailAsync(Guid customerId, string newEmail, ExecutionContext context, CancellationToken cancellationToken)
    {
        _rbac.EnsureWriteAllowed("customers.updateEmail", context);
        BusinessValidators.EnsureWriteAuthorized("customers.updateEmail", context, new[] { "admin", "ops" });
        BusinessValidators.ValidateEmail(newEmail);

        var customer = await _customersRepository.GetByIdAsync(context.TenantId, customerId, cancellationToken)
            ?? throw new KeyNotFoundException("Customer not found.");

        BusinessValidators.EnsureCustomerIsActive(customer);

        var otherWithEmail = await _customersRepository.GetByEmailAsync(context.TenantId, newEmail, cancellationToken);
        if (otherWithEmail is not null && otherWithEmail.Id != customerId)
        {
            throw new InvalidOperationException("Email already in use.");
        }

        if (string.Equals(customer.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Email is unchanged.");
        }

        var plan = await _confirmationPlanService.CreatePlanAsync(
            "customers.updateEmail",
            context,
            new Dictionary<string, string>
            {
                ["customerId"] = customerId.ToString(),
                ["email"] = newEmail
            },
            cancellationToken);

        return new
        {
            confirmation_id = plan.Id,
            expires_at = plan.ExpiresAt,
            preview = new
            {
                customer.Id,
                customer.Name,
                current_email = customer.Email,
                new_email = newEmail
            }
        };
    }

    public async Task<Customer> CommitUpdateEmailAsync(string confirmationId, ExecutionContext context, CancellationToken cancellationToken)
    {
        _rbac.EnsureWriteAllowed("customers.updateEmail", context);
        BusinessValidators.EnsureWriteAuthorized("customers.updateEmail", context, new[] { "admin", "ops" });

        var plan = await _confirmationPlanService.ConsumePlanAsync(confirmationId, "customers.updateEmail", context, cancellationToken);
        if (!plan.Data.TryGetValue("customerId", out var customerIdText) || !Guid.TryParse(customerIdText, out var customerId))
        {
            throw new InvalidOperationException("Invalid confirmation payload.");
        }

        if (!plan.Data.TryGetValue("email", out var newEmail) || string.IsNullOrWhiteSpace(newEmail))
        {
            throw new InvalidOperationException("Invalid confirmation payload.");
        }

        BusinessValidators.ValidateEmail(newEmail);

        var customer = await _customersRepository.GetByIdAsync(context.TenantId, customerId, cancellationToken)
            ?? throw new KeyNotFoundException("Customer not found.");

        BusinessValidators.EnsureCustomerIsActive(customer);

        var otherWithEmail = await _customersRepository.GetByEmailAsync(context.TenantId, newEmail, cancellationToken);
        if (otherWithEmail is not null && otherWithEmail.Id != customerId)
        {
            throw new InvalidOperationException("Email already in use.");
        }

        await _unitOfWork.ExecuteTransactionalAsync(async ct =>
        {
            customer.Email = newEmail;
            customer.UpdatedAt = DateTimeOffset.UtcNow;
            await _customersRepository.UpdateAsync(customer, ct);
        }, cancellationToken);

        await _auditLogger.LogToolExecutionAsync(
            context,
            "customers.updateEmail",
            new { confirmationId },
            new { customer.Id, customer.Email, customer.UpdatedAt },
            cancellationToken);

        return customer;
    }
}
