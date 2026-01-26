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
        ["atomic.query.execute"] = ReadRoles,

        // Entity Graph + Document Search
        ["atomic.graph.search"] = ReadRoles,
        ["atomic.graph.get"] = ReadRoles,
        ["atomic.doc.search"] = ReadRoles
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

    /// <summary>
    /// Returns true if the tool has an explicit RBAC mapping.
    /// This is used by startup validation to fail fast on misconfiguration.
    /// </summary>
    public bool IsToolConfigured(string toolName)
        => ToolRoles.ContainsKey(toolName);
}
