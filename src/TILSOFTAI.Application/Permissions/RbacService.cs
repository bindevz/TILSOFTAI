using System.Security;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Application.Permissions;

public sealed class RbacService
{
    private static readonly string[] ReadRoles = { "admin", "ops", "analyst", "user" };
    private static readonly string[] WriteRoles = { "admin", "ops" };

    private static readonly Dictionary<string, string[]> ToolRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Analytics / Atomic
        ["analytics.run"] = ReadRoles,
        ["atomic.catalog.search"] = ReadRoles,
        ["atomic.query.execute"] = ReadRoles
    };

    public void EnsureReadAllowed(string toolName, TSExecutionContext context)
    {
        if (!ToolRoles.TryGetValue(toolName, out var roles) || !roles.Any(context.IsInRole))
            throw new SecurityException($"Tool {toolName} not permitted for user.");
    }

    public void EnsureWriteAllowed(string toolName, TSExecutionContext context)
    {
        if (!ToolRoles.TryGetValue(toolName, out var roles))
            throw new SecurityException($"Tool {toolName} not permitted for user.");

        if (!WriteRoles.Any(context.IsInRole) || !roles.Any(context.IsInRole))
            throw new SecurityException($"Tool {toolName} not permitted for user.");
    }
}
