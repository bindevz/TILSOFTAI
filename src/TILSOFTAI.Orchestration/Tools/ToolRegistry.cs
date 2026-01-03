using System.Text.Json;

namespace TILSOFTAI.Orchestration.Tools;

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _definitions;

    public ToolRegistry()
    {
        _definitions = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["models.search"] = new(
                Name: "models.search",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("models.search", args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "filters", "page", "pageSize"
                }),

            ["models.count"] = new(
                Name: "models.count",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("models.count", args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "filters"
                }),

            ["models.stats"] = new(
                Name: "models.stats",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("models.stats", args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "filters",
                    "topN"
                }),

            ["models.options"] = new(
                Name: "models.options",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("models.options", args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "modelId",
                    "includeConstraints"
                }),

            ["models.get"] = new(
                Name: "models.get",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("models.get", args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "modelId" }),

            ["models.attributes.list"] = new(
                Name: "models.attributes.list",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("models.attributes.list", args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "modelId" }),

            ["models.price.analyze"] = new(
                Name: "models.price.analyze",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("models.price.analyze", args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "modelId" }),

            ["models.create.prepare"] = new(
                Name: "models.create.prepare",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("models.create.prepare", args).ToObject(),
                RequiresWrite: true,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "category", "basePrice", "attributes" }),

            ["models.create.commit"] = new(
                Name: "models.create.commit",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("models.create.commit", args).ToObject(),
                RequiresWrite: true,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "confirmationId" }),

            //System fillter
            ["filters.catalog"] = new(
                Name: "filters.catalog",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("filters.catalog", args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "resource",
                    "includeValues"
                }),

            // System: actions catalog (Stage 2)
            ["actions.catalog"] = new(
                Name: "actions.catalog",
                Validator: args => ToolSchemas.DynamicIntentValidator.Validate("actions.catalog", args).ToObject(),
                RequiresWrite: false,
                AllowedArguments: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "action",
                    "includeExamples"
                })

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
