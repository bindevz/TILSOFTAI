using System.Text.RegularExpressions;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Application.Validators;

public static class BusinessValidators
{
    private const int MaxPageSize = 500;
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void ValidateOrderQuery(OrderQuery query)
    {
        if (query.PageNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(query.PageNumber), "Page number must be positive.");
        }

        if (query.PageSize <= 0 || query.PageSize > MaxPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(query.PageSize), $"Page size must be between 1 and {MaxPageSize}.");
        }

        if (query.StartDate.HasValue && query.EndDate.HasValue && query.StartDate > query.EndDate)
        {
            throw new ArgumentException("Start date must be earlier than end date.");
        }
    }

    public static void EnsureCustomerIsActive(Customer customer)
    {
        if (!customer.IsActive)
        {
            throw new InvalidOperationException("Customer is inactive.");
        }
    }

    public static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
        {
            throw new ArgumentException("Invalid email format.", nameof(email));
        }
    }
}
