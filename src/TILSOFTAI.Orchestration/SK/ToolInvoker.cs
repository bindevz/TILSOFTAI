using System.Diagnostics;
using System.Security;
using System.Text.Json;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Orchestration.Contracts;
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
        // IMPORTANT: Do not throw for expected errors (validation/forbidden/contract).
        // Returning a structured envelope keeps the LLM from retrying the same tool call.

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(argsObj, _json));
        var args = doc.RootElement.Clone();

        var sw = Stopwatch.StartNew();
        var requiresWrite = false;
        object? normalizedIntent = null;

        // Stage 2: envelope policy + telemetry
        var policy = EnvelopePolicyV1.Deny(_ctx.Context, "UNSPECIFIED");
        EnvelopeSourceV1? source = null;
        IReadOnlyList<EnvelopeEvidenceItemV1>? evidence = null;

        try
        {
            if (!_registry.IsWhitelisted(toolName))
            {
                policy = EnvelopePolicyV1.Deny(_ctx.Context, "TOOL_NOT_ALLOWED");
                return EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                    telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                    policy: policy,
                    code: "TOOL_NOT_ALLOWED",
                    message: "Tool not allowed.");
            }

            if (!_registry.TryValidate(toolName, args, out var intent, out var validationError, out requiresWrite))
            {
                policy = EnvelopePolicyV1.Deny(_ctx.Context, "VALIDATION_ERROR");
                return EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                    telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                    policy: policy,
                    code: "VALIDATION_ERROR",
                    message: validationError ?? "Invalid arguments.",
                    details: new { args });
            }

            // RBAC
            try
            {
                if (requiresWrite) _rbac.EnsureWriteAllowed(toolName, _ctx.Context);
                else _rbac.EnsureReadAllowed(toolName, _ctx.Context);

                policy = EnvelopePolicyV1.Allow(_ctx.Context);
            }
            catch (SecurityException)
            {
                policy = EnvelopePolicyV1.Deny(_ctx.Context, "FORBIDDEN");
                return EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                    telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                    policy: policy,
                    code: "FORBIDDEN",
                    message: "Forbidden.");
            }

            var dispatchResult = await _dispatcher.DispatchAsync(toolName, intent!, _ctx.Context, ct);
            normalizedIntent = dispatchResult.NormalizedIntent;
            source = dispatchResult.Extras.Source;
            evidence = dispatchResult.Extras.Evidence;

            if (!dispatchResult.Result.Success)
            {
                policy = EnvelopePolicyV1.Deny(_ctx.Context, "TOOL_EXECUTION_FAILED");
                return EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                    telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                    policy: policy,
                    code: "TOOL_EXECUTION_FAILED",
                    message: dispatchResult.Result.Message,
                    normalizedIntent: normalizedIntent,
                    source: source,
                    evidence: evidence);
            }

            return EnvelopeV1.Success(toolName, requiresWrite, _ctx.Context,
                telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                policy: policy,
                normalizedIntent: normalizedIntent,
                message: dispatchResult.Result.Message,
                data: dispatchResult.Result.Data,
                source: source,
                evidence: evidence);
        }
        catch (ResponseContractException ex)
        {
            // Contract/guardrail error.
            var code = string.Equals(ex.Message, "Forbidden.", StringComparison.OrdinalIgnoreCase) ? "FORBIDDEN" : "CONTRACT_ERROR";
            policy = EnvelopePolicyV1.Deny(_ctx.Context, code);
            return EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                policy: policy,
                code: code,
                message: ex.Message,
                normalizedIntent: normalizedIntent,
                source: source,
                evidence: evidence);
        }
        catch (Exception ex)
        {
            policy = EnvelopePolicyV1.Deny(_ctx.Context, "INTERNAL_ERROR");
            return EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                policy: policy,
                code: "INTERNAL_ERROR",
                message: "Exception while invoking function.",
                details: new { exception = ex.GetType().Name, ex.Message },
                normalizedIntent: normalizedIntent,
                source: source,
                evidence: evidence);
        }
    }
}
