using System.Security;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Application.Permissions;

public sealed class RbacService
{
    private static readonly string[] ReadRoles = { "admin", "ops", "analyst", "user" };
    private static readonly string[] WriteRoles = { "admin", "ops" };
    private static readonly Dictionary<string, string[]> ToolRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        //SYSTEM FILLTERS
        ["filters.catalog"] = ReadRoles,


        ["orders.query"] = ReadRoles,
        ["orders.summary"] = ReadRoles,
        ["orders.create.prepare"] = WriteRoles,
        ["orders.create.commit"] = WriteRoles,

        ["customers.updateEmail"] = WriteRoles,
        ["customers.search"] = ReadRoles,

        ["models.search"] = ReadRoles,
        ["models.get"] = ReadRoles,
        ["models.attributes.list"] = ReadRoles,
        ["models.price.analyze"] = ReadRoles,
        ["models.create.prepare"] = WriteRoles,
        ["models.create.commit"] = WriteRoles,
        ["models.count"] = ReadRoles
    };

    public void EnsureReadAllowed(string toolName, TSExecutionContext context)
    {
        if (!ToolRoles.TryGetValue(toolName, out var roles) || !roles.Any(context.IsInRole))
        {
            throw new SecurityException($"Tool {toolName} not permitted for user.");
        }
    }

    public void EnsureWriteAllowed(string toolName, TSExecutionContext context)
    {
        if (!ToolRoles.TryGetValue(toolName, out var roles))
        {
            throw new SecurityException($"Tool {toolName} not permitted for user.");
        }

        if (!WriteRoles.Any(context.IsInRole) || !roles.Any(context.IsInRole))
        {
            throw new SecurityException($"Tool {toolName} not permitted for user.");
        }
    }
}
