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
            ["orders.query"] = new(
                Name: "orders.query",
                Validator: args => OrdersSchemas.ValidateOrderQuery(args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "customerId", "status", "startDate", "endDate", "page", "pageSize", "season", "metric" }),
            ["orders.summary"] = new(
                Name: "orders.summary",
                Validator: args => OrdersSchemas.ValidateOrderSummary(args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "customerId", "status", "startDate", "endDate", "season", "metric" }),
            ["customers.updateEmail"] = new(
                Name: "customers.updateEmail",
                Validator: args => CustomersSchemas.ValidateUpdateEmail(args).ToObject(),
                RequiresWrite: true,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "customerId", "email", "confirmationId" }),
            ["models.search"] = new(
                Name: "models.search",
                Validator: args => ModelsSchemas.ValidateSearch(args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // canonical fields (khuyến nghị dùng)
                    "rangeName", "modelCode", "modelName", "season", "collection",

                    // paging
                    "page", "pageSize",

                    // backward-compatible aliases (nếu trước đây LLM hay dùng)
                    "category", "name"
                }),

            ["models.get"] = new(
                Name: "models.get",
                Validator: args => ModelsSchemas.ValidateGet(args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "modelId" }),
            ["models.attributes.list"] = new(
                Name: "models.attributes.list",
                Validator: args => ModelsSchemas.ValidateAttributes(args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "modelId" }),
            ["models.price.analyze"] = new(
                Name: "models.price.analyze",
                Validator: args => ModelsSchemas.ValidatePrice(args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "modelId" }),
            ["models.create.prepare"] = new(
                Name: "models.create.prepare",
                Validator: args => ModelsSchemas.ValidateCreatePrepare(args).ToObject(),
                RequiresWrite: true,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "category", "basePrice", "attributes" }),
            ["models.create.commit"] = new(
                Name: "models.create.commit",
                Validator: args => ModelsSchemas.ValidateCreateCommit(args).ToObject(),
                RequiresWrite: true,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "confirmationId" }),
            ["customers.search"] = new(
                Name: "customers.search",
                Validator: args => CustomersSchemas.ValidateSearch(args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "query", "page", "pageSize" }),
            ["orders.create.prepare"] = new(
                Name: "orders.create.prepare",
                Validator: args => OrdersSchemas.ValidateOrderCreatePrepare(args).ToObject(),
                RequiresWrite: true,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "customerId", "modelId", "color", "quantity" }),
            ["orders.create.commit"] = new(
                Name: "orders.create.commit",
                Validator: args => OrdersSchemas.ValidateOrderCreateCommit(args).ToObject(),
                RequiresWrite: true,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "confirmationId" })
        };
    }

    public IEnumerable<string> GetToolNames() => _definitions.Keys;

    public bool IsWhitelisted(string toolName) => _definitions.ContainsKey(toolName);

    public bool TryValidate(string toolName, JsonElement arguments, out object? intent, out string? error, out bool requiresWrite)
    {
        intent = null;
        requiresWrite = false;
        if (!_definitions.TryGetValue(toolName, out var definition))
        {
            error = "Tool not allowed.";
            return false;
        }

        if (!AreArgumentsAllowed(arguments, definition.AllowedArguments))
        {
            error = "Arguments contain unexpected fields.";
            return false;
        }

        try
        {
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
        catch (ArgumentException ex)
        {
            error = ex.Message;
            intent = null;
            requiresWrite = false;
            return false;
        }
        catch (Exception)
        {
            error = "Invalid arguments.";
            intent = null;
            requiresWrite = false;
            return false;
        }
    }

    private static bool AreArgumentsAllowed(JsonElement arguments, IReadOnlyCollection<string> allowed)
    {
        return arguments.ValueKind == JsonValueKind.Object &&
               arguments.EnumerateObject().All(p => allowed.Contains(p.Name));
    }
}

public sealed record ToolDefinition(
    string Name,
    Func<JsonElement, ValidationResult<object>> Validator,
    bool RequiresWrite,
    IReadOnlyCollection<string> AllowedArguments);

internal static class ToolValidationExtensions
{
    public static ValidationResult<object> ToObject<T>(this ValidationResult<T> input)
    {
        return input.IsValid && input.Value is not null
            ? ValidationResult<object>.Success(input.Value)
            : ValidationResult<object>.Fail(input.Error ?? "Invalid arguments.");
    }
}
