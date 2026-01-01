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

    // Tune these values based on your tool surface area and typical workflows.
    private const int MaxAutoInvokesPerRequest = 12;
    private const int MaxRepeatSameCall = 3;

    public AutoInvocationCircuitBreakerFilter(ExecutionContextAccessor ctx) => _ctx = ctx;

    public async Task OnAutoFunctionInvocationAsync(
        AutoFunctionInvocationContext context,
        Func<AutoFunctionInvocationContext, Task> next)
    {
        _ctx.AutoInvokeCount++;

        if (_ctx.AutoInvokeCount > MaxAutoInvokesPerRequest)
        {
            _ctx.CircuitBreakerTripped = true;
            _ctx.CircuitBreakerReason = "too_many_calls";

            // Do not return user-facing text that the model might echo.
            context.Result = new FunctionResult(
                context.Function,
                "{\"circuit_breaker\":true,\"reason\":\"too_many_calls\"}");

            context.Terminate = true;
            return;
        }

        var signature = BuildSignature(context);
        _ctx.AutoInvokeSignatureCounts.TryGetValue(signature, out var n);
        n++;
        _ctx.AutoInvokeSignatureCounts[signature] = n;

        if (n >= MaxRepeatSameCall)
        {
            _ctx.CircuitBreakerTripped = true;
            _ctx.CircuitBreakerReason = "repeated_same_call";

            // Do not return user-facing text that the model might echo.
            context.Result = new FunctionResult(
                context.Function,
                "{\"circuit_breaker\":true,\"reason\":\"repeated_same_call\"}");

            context.Terminate = true;
            return;
        }

        await next(context);
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
