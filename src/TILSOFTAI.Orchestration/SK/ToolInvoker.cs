using System.Security;
using System.Text.Json;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.SK;

public sealed class ToolInvoker
{
    private readonly ToolRegistry _registry;
    private readonly ToolDispatcher _dispatcher;
    private readonly RbacService _rbac;
    private readonly ExecutionContextAccessor _ctx;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ToolInvoker(
        ToolRegistry registry,
        ToolDispatcher dispatcher,
        RbacService rbac,
        ExecutionContextAccessor ctx)
    {
        _registry = registry;
        _dispatcher = dispatcher;
        _rbac = rbac;
        _ctx = ctx;
    }

    public async Task<object> ExecuteAsync(string toolName, object argsObj, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(argsObj, _json));
        var args = doc.RootElement.Clone();

        if (!_registry.IsWhitelisted(toolName))
        {
            throw new ResponseContractException("Tool not allowed.");
        }

        if (!_registry.TryValidate(toolName, args, out var intent, out var validationError, out var requiresWrite))
        {
            throw new ResponseContractException(validationError ?? "Invalid arguments.");
        }

        // RBAC như pipeline cũ
        try
        {
            if (requiresWrite) _rbac.EnsureWriteAllowed(toolName, _ctx.Context);
            else _rbac.EnsureReadAllowed(toolName, _ctx.Context);
        }
        catch (SecurityException)
        {
            throw new ResponseContractException("Forbidden.");
        }

        var invocation = new ToolInvocation(toolName, args);
        var dispatchResult = await _dispatcher.DispatchAsync(toolName, intent!, _ctx.Context, ct);

        if (!dispatchResult.Result.Success)
        {
            throw new ResponseContractException("Tool execution failed.");
        }

        // Trả đúng “evidence” data như hiện tại
        return new
        {
            tool = toolName,
            normalizedIntent = dispatchResult.NormalizedIntent,
            message = dispatchResult.Result.Message,
            data = dispatchResult.Result.Data
        };
    }
}
