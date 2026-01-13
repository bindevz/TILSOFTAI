using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using System.Text;
using TILSOFTAI.Orchestration.SK;

namespace TILSOFTAI.Orchestration.SK.Governance;

/// <summary>
/// Server-side guardrail to prevent infinite/degenerate tool-calling loops.
/// Does not rely on any system prompt sentinel.
/// </summary>
public sealed class AutoInvocationCircuitBreakerFilter : IAutoFunctionInvocationFilter
{
    private readonly ExecutionContextAccessor _ctx;
    private readonly ILogger<AutoInvocationCircuitBreakerFilter> _logger;

    // Tune these values based on your tool surface area and typical workflows.
    private const int MaxAutoInvokesPerRequest = 12;
    private const int MaxRepeatSameCall = 3;

    public AutoInvocationCircuitBreakerFilter(ExecutionContextAccessor ctx, ILogger<AutoInvocationCircuitBreakerFilter> logger)
    {
        _ctx = ctx;
        _logger = logger ?? NullLogger<AutoInvocationCircuitBreakerFilter>.Instance;
    }

    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        _ctx.AutoInvokeCount++;

        var signature = BuildSignature(context);
        _ctx.AutoInvokeSignatureCounts.TryGetValue(signature, out var existing);
        var sigCount = existing + 1;
        _ctx.AutoInvokeSignatureCounts[signature] = sigCount;

        // Debug-level by default; promote to Warning only when tripping.
        _logger.LogDebug(
            "CircuitBreaker observe req={RequestId} trace={TraceId} tool={Tool} autoInvokes={AutoInvokes} sigCount={SigCount}",
            _ctx.Context?.RequestId,
            _ctx.Context?.TraceId,
            $"{context.Function.PluginName}.{context.Function.Name}",
            _ctx.AutoInvokeCount,
            sigCount);

        if (_ctx.AutoInvokeCount > MaxAutoInvokesPerRequest)
        {
            Trip(context, reason: "too_many_calls", signature: signature, sigCount: sigCount);
            return;
        }

        if (sigCount > MaxRepeatSameCall)
        {
            Trip(context, reason: "repeated_same_call", signature: signature, sigCount: sigCount);
            return;
        }

        await next(context);
    }

    private void Trip(AutoFunctionInvocationContext context, string reason, string signature, int sigCount)
    {
        _ctx.CircuitBreakerTripped = true;
        _ctx.CircuitBreakerReason = reason;

        _logger.LogWarning(
            "CircuitBreaker tripped req={RequestId} trace={TraceId} reason={Reason} autoInvokes={AutoInvokes} sigCount={SigCount} sig={Signature}",
            _ctx.Context?.RequestId,
            _ctx.Context?.TraceId,
            reason,
            _ctx.AutoInvokeCount,
            sigCount,
            signature);

        // Do not return user-facing text that the model might echo.
        context.Result = new FunctionResult(
            context.Function,
            $"{{\"circuit_breaker\":true,\"reason\":\"{reason}\"}}");

        context.Terminate = true;
    }

    private static string BuildSignature(AutoFunctionInvocationContext context)
    {
        // signature = plugin.function + normalized args
        var sb = new StringBuilder();
        sb.Append(context.Function.PluginName);
        sb.Append('.');
        sb.Append(context.Function.Name);
        sb.Append('(');

        foreach (var kv in context.Arguments.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            sb.Append(kv.Key);
            sb.Append('=');
            sb.Append(kv.Value?.ToString() ?? "null");
            sb.Append(';');
        }

        sb.Append(')');
        return sb.ToString();
    }
}
