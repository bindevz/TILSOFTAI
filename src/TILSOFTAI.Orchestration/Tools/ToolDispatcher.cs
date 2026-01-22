using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Contracts.Validation;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Tools;

/// <summary>
/// Dispatches tool calls to registered handlers.
///
/// This intentionally contains no business logic. Business logic lives in module handlers.
/// </summary>
public sealed class ToolDispatcher
{
    private readonly IReadOnlyDictionary<string, IToolHandler> _handlers;

    public ToolDispatcher(IEnumerable<IToolHandler> handlers)
    {
        // Fail fast on duplicate tool names to keep governance predictable.
        var dict = new Dictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in handlers)
        {
            if (string.IsNullOrWhiteSpace(h.ToolName))
                throw new InvalidOperationException("Tool handler has empty ToolName.");

            if (!dict.TryAdd(h.ToolName, h))
                throw new InvalidOperationException($"Duplicate tool handler registered: {h.ToolName}");
        }

        _handlers = dict;
    }

    public Task<ToolDispatchResult> DispatchAsync(
        string toolName,
        object intent,
        TSExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(toolName, out var handler))
            throw new ResponseContractException("Tool not supported.");

        return handler.HandleAsync(intent, context, cancellationToken);
    }
}
