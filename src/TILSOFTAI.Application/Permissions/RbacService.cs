using System.Security;

namespace TILSOFTAI.Application.Permissions;

public sealed class RbacService
{
    private static readonly string[] ReadRoles = { "admin", "ops", "analyst", "viewer" };
    private static readonly string[] WriteRoles = { "admin", "ops" };

    public void EnsureReadAllowed(string toolName, TILSOFTAI.Domain.ValueObjects.ExecutionContext context)
    {
        if (!context.Roles.Any(role => ReadRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
        {
            throw new SecurityException($"Tool {toolName} not permitted for user.");
        }
    }

    public void EnsureWriteAllowed(string toolName, TILSOFTAI.Domain.ValueObjects.ExecutionContext context)
    {
        if (!context.Roles.Any(role => WriteRoles.Contains(role, StringComparer.OrdinalIgnoreCase)))
        {
            throw new SecurityException($"Tool {toolName} not permitted for user.");
        }
    }
}
