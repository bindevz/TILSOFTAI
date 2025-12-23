using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Application.Validators;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;

namespace TILSOFTAI.Application.Services;

public sealed class CustomersService
{
    private readonly ICustomersRepository _customersRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly RbacService _rbac;

    public CustomersService(ICustomersRepository customersRepository, IUnitOfWork unitOfWork, RbacService rbac)
    {
        _customersRepository = customersRepository;
        _unitOfWork = unitOfWork;
        _rbac = rbac;
    }

    public async Task<Customer> UpdateEmailAsync(Guid customerId, string newEmail, TILSOFTAI.Domain.ValueObjects.ExecutionContext context, CancellationToken cancellationToken)
    {
        _rbac.EnsureWriteAllowed("customers.updateEmail", context);
        BusinessValidators.ValidateEmail(newEmail);

        var customer = await _customersRepository.GetByIdAsync(context.TenantId, customerId, cancellationToken)
            ?? throw new KeyNotFoundException("Customer not found.");

        BusinessValidators.EnsureCustomerIsActive(customer);

        var otherWithEmail = await _customersRepository.GetByEmailAsync(context.TenantId, newEmail, cancellationToken);
        if (otherWithEmail is not null && otherWithEmail.Id != customerId)
        {
            throw new InvalidOperationException("Email already in use.");
        }

        if (!string.Equals(customer.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            await _unitOfWork.ExecuteTransactionalAsync(async ct =>
            {
                customer.Email = newEmail;
                customer.UpdatedAt = DateTimeOffset.UtcNow;
                await _customersRepository.UpdateAsync(customer, ct);
            }, cancellationToken);
        }

        return customer;
    }
}
