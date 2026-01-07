using System.Text.Json;

namespace TILSOFTAI.Orchestration.Tools;

public interface IToolRegistrationProvider
{
    IEnumerable<ToolDefinition> GetToolDefinitions();
}

public sealed class ToolRegistry
{
    private readonly Dictionary<string, ToolDefinition> _definitions;

    public ToolRegistry(IEnumerable<IToolRegistrationProvider> providers)
    {
        _definitions = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in providers)
        {
            foreach (var d in p.GetToolDefinitions())
            {
                if (string.IsNullOrWhiteSpace(d.Name))
                    throw new InvalidOperationException("ToolDefinition has empty Name.");

                if (!_definitions.TryAdd(d.Name, d))
                    throw new InvalidOperationException($"Duplicate tool definition registered: {d.Name}");
            }
        }
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
