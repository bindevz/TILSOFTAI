using System.Text.Json;
using TILSOFTAI.Orchestration.Tools.ToolSchemas;

namespace TILSOFTAI.Orchestration.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _definitions;

    public ToolRegistry()
    {
        _definitions = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["orders.query"] = new("orders.query", args => OrdersSchemas.ValidateOrderQuery(args).ToObject(), RequiresWrite: false),
            ["orders.summary"] = new("orders.summary", args => OrdersSchemas.ValidateOrderSummary(args).ToObject(), RequiresWrite: false),
            ["customers.updateEmail"] = new("customers.updateEmail", args => CustomersSchemas.ValidateUpdateEmail(args).ToObject(), RequiresWrite: true)
        };
    }

    public IEnumerable<string> GetToolNames() => _definitions.Keys;

    public bool TryValidate(string toolName, JsonElement arguments, out object? intent, out string? error, out bool requiresWrite)
    {
        intent = null;
        requiresWrite = false;
        if (!_definitions.TryGetValue(toolName, out var definition))
        {
            error = "Tool not allowed.";
            return false;
        }

        var validation = definition.Validator(arguments);
        requiresWrite = definition.RequiresWrite;
        if (!validation.IsValid)
        {
            error = validation.Error ?? "Invalid arguments.";
            return false;
        }

        intent = validation.Value;
        error = null;
        return true;
    }
}

public sealed record ToolDefinition(string Name, Func<JsonElement, ValidationResult<object>> Validator, bool RequiresWrite);

internal static class ToolValidationExtensions
{
    public static ValidationResult<object> ToObject<T>(this ValidationResult<T> input)
    {
        return input.IsValid && input.Value is not null
            ? ValidationResult<object>.Success(input.Value)
            : ValidationResult<object>.Fail(input.Error ?? "Invalid arguments.");
    }
}
