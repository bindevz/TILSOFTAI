using System.Security;
using System.Text.RegularExpressions;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Application.Validators;

public static class BusinessValidators
{
    private const int MaxPageSize = 500;
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email))
        {
            throw new ArgumentException("Invalid email format.", nameof(email));
        }
    }

    public static void EnsureWriteAuthorized(string toolName, TSExecutionContext context, IEnumerable<string> allowedRoles)
    {
        if (!allowedRoles.Any(context.IsInRole))
        {
            throw new SecurityException($"Tool {toolName} not permitted for user.");
        }
    }

    public static void ValidatePage(int page, int size, int maxSize = 500)
    {
        if (page <= 0 || size <= 0 || size > maxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(size), $"Invalid pagination. page>0, 0<size<={maxSize}");
        }
    }
}
