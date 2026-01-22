using System.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Security;
using System.Text.Json;
using TILSOFTAI.Orchestration.Chat;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Contracts.Evidence;
using TILSOFTAI.Orchestration.Contracts.Validation;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.SK;

public sealed class ToolInvoker
{
    private readonly ToolRegistry _registry;
    private readonly ILogger<ToolInvoker> _logger;
    private readonly ToolDispatcher _dispatcher;
    private readonly RbacService _rbac;
    private readonly ExecutionContextAccessor _ctx;
    private readonly IResponseSchemaValidator _responseSchemaValidator;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ToolInvoker(
        ToolRegistry registry,
        ToolDispatcher dispatcher,
        RbacService rbac,
        ExecutionContextAccessor ctx,
        IResponseSchemaValidator responseSchemaValidator,
        ILogger<ToolInvoker> logger)
    {
        _registry = registry;
        _dispatcher = dispatcher;
        _rbac = rbac;
        _ctx = ctx;
        _responseSchemaValidator = responseSchemaValidator;
        _logger = logger;
    }

    public async Task<object> ExecuteAsync(string toolName, object argsObj, IReadOnlySet<string> allowedToolNames, CancellationToken ct)
    {
        // IMPORTANT: Do not throw for expected errors (validation/forbidden/contract).
        // Returning a structured envelope keeps the LLM from retrying the same tool call.

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(argsObj, _json));
        var args = doc.RootElement.Clone();

        var sw = Stopwatch.StartNew();

        var argKeys = args.ValueKind == JsonValueKind.Object
            ? string.Join(",", args.EnumerateObject().Select(p => p.Name))
            : args.ValueKind.ToString();

        _logger.LogInformation("ToolInvoker start req={RequestId} trace={TraceId} tool={Tool} argKeys={ArgKeys}",
            _ctx.Context?.RequestId, _ctx.Context?.TraceId, toolName, argKeys);

        var requiresWrite = false;
        object? normalizedIntent = null;

        // Stage 2: envelope policy + telemetry
        var policy = EnvelopePolicyV1.Deny(_ctx.Context, "UNSPECIFIED");
        EnvelopeSourceV1? source = null;
        IReadOnlyList<EnvelopeEvidenceItemV1>? evidence = null;

        object? lastPayload = null;
        try
        {
            if (allowedToolNames is null || !allowedToolNames.Contains(toolName))
            {
                var notAllowed = new
                {
                    Success = false,
                    Error = "TOOL_NOT_ALLOWED",
                    Details = "Tool is not exposed in this pipeline."
                };

                policy = EnvelopePolicyV1.Deny(_ctx.Context, "TOOL_NOT_ALLOWED");
                _logger.LogInformation("ToolInvoker return req={RequestId} trace={TraceId} tool={Tool} ok=false code=TOOL_NOT_ALLOWED ms={Ms}", _ctx.Context?.RequestId, _ctx.Context?.TraceId, toolName, sw.ElapsedMilliseconds);
                return LogAndReturn(EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                    telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                    policy: policy,
                    code: "TOOL_NOT_ALLOWED",
                    message: "Tool is not exposed in this pipeline.",
                    details: notAllowed,
                    evidence: new[]
                    {
                        new EnvelopeEvidenceItemV1
                        {
                            Id = "tool_not_allowed",
                            Type = "error",
                            Title = "Tool not exposed",
                            Payload = notAllowed
                        }
                    }), sw.ElapsedMilliseconds);
            }

            if (!_registry.IsWhitelisted(toolName))
            {
                policy = EnvelopePolicyV1.Deny(_ctx.Context, "TOOL_NOT_ALLOWED");
                _logger.LogInformation("ToolInvoker return req={RequestId} trace={TraceId} tool={Tool} ok=false code=TOOL_NOT_ALLOWED ms={Ms}", _ctx.Context?.RequestId, _ctx.Context?.TraceId, toolName, sw.ElapsedMilliseconds);
                return LogAndReturn(
                    EnvelopeV1.Failure(
                        toolName,
                        requiresWrite,
                        _ctx.Context,
                        telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                        policy: policy,
                        code: "TOOL_NOT_ALLOWED",
                        message: "Tool not allowed.",
                        details: new { allowed = allowedToolNames.OrderBy(x => x).Take(20).ToArray() }
                    ),
                    sw.ElapsedMilliseconds);
            }

            if (!_registry.TryValidate(toolName, args, out var intent, out var validationError, out requiresWrite))
            {
                policy = EnvelopePolicyV1.Deny(_ctx.Context, "VALIDATION_ERROR");
                _logger.LogInformation("ToolInvoker return req={RequestId} trace={TraceId} tool={Tool} ok=false code=VALIDATION_ERROR ms={Ms}", _ctx.Context?.RequestId, _ctx.Context?.TraceId, toolName, sw.ElapsedMilliseconds);
                return LogAndReturn(EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                    telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                    policy: policy,
                    code: "VALIDATION_ERROR",
                    message: validationError ?? "Invalid arguments.",
                    details: new { args }), sw.ElapsedMilliseconds);
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
                _logger.LogInformation("ToolInvoker return req={RequestId} trace={TraceId} tool={Tool} ok=false code=FORBIDDEN ms={Ms}", _ctx.Context?.RequestId, _ctx.Context?.TraceId, toolName, sw.ElapsedMilliseconds);
                return LogAndReturn(EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                    telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                    policy: policy,
                    code: "FORBIDDEN",
                    message: "Forbidden."), sw.ElapsedMilliseconds);
            }

            var dispatchResult = await _dispatcher.DispatchAsync(toolName, intent!, _ctx.Context, ct);
            normalizedIntent = dispatchResult.NormalizedIntent;
            source = dispatchResult.Extras.Source;
            evidence = dispatchResult.Extras.Evidence;

            if (!dispatchResult.Result.Success)
            {
                var failure = ExtractToolFailure(dispatchResult.Result);
                policy = EnvelopePolicyV1.Deny(_ctx.Context, failure.Code);
                _logger.LogInformation("ToolInvoker return req={RequestId} trace={TraceId} tool={Tool} ok=false code={Code} ms={Ms}", _ctx.Context?.RequestId, _ctx.Context?.TraceId, toolName, failure.Code, sw.ElapsedMilliseconds);
                return LogAndReturn(EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                    telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                    policy: policy,
                    code: failure.Code,
                    message: failure.Message,
                    details: failure.Details,
                    normalizedIntent: normalizedIntent,
                    source: source,
                    evidence: evidence), sw.ElapsedMilliseconds);
            }

            // Runtime response contract validation (ver25)
            // Ensures tool handlers cannot drift away from governance/contracts.
            lastPayload = dispatchResult.Result.Data;
            _responseSchemaValidator.ValidateOrThrow(lastPayload, toolName);

            // Fallback (anti-loop): if the handler did not attach evidence, synthesize a compact one.
            // Some clients/UI layers render only envelope.evidence; returning an empty list causes
            // the assistant to retry tools and the user sees no answer.
            if (evidence is null || evidence.Count == 0)
                evidence = EvidenceFallbackBuilder.Build(lastPayload);

            _logger.LogInformation("ToolInvoker return req={RequestId} trace={TraceId} tool={Tool} ok=true ms={Ms}", _ctx.Context?.RequestId, _ctx.Context?.TraceId, toolName, sw.ElapsedMilliseconds);
            return LogAndReturn(EnvelopeV1.Success(toolName, requiresWrite, _ctx.Context,
                telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                policy: policy,
                normalizedIntent: normalizedIntent,
                message: dispatchResult.Result.Message,
                data: dispatchResult.Result.Data,
                source: source,
                evidence: evidence), sw.ElapsedMilliseconds);
        }
        catch (ResponseContractException ex)
        {
            // Contract/guardrail error.
            // This is non-retryable: the payload emitted by the server does not match governance schema.
            // Returning a structured failure envelope prevents the LLM from repeatedly calling the same tool
            // while also keeping the API stable (no hard throw).
            policy = EnvelopePolicyV1.Deny(_ctx.Context, "CONTRACT_ERROR");
                _logger.LogInformation("ToolInvoker return req={RequestId} trace={TraceId} tool={Tool} ok=false code=CONTRACT_ERROR ms={Ms}", _ctx.Context?.RequestId, _ctx.Context?.TraceId, toolName, sw.ElapsedMilliseconds);
            return LogAndReturn(EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                policy: policy,
                code: "CONTRACT_ERROR",
                message: ex.Message,
                details: new
                {
                    kind = "response.schema.validation",
                    tool = toolName,
                    payloadKind = TryGetPayloadKind(lastPayload),
                    payloadSchemaVersion = TryGetPayloadSchemaVersion(lastPayload)
                },
                normalizedIntent: normalizedIntent,
                source: source,
                evidence: evidence), sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            policy = EnvelopePolicyV1.Deny(_ctx.Context, "INTERNAL_ERROR");
                _logger.LogInformation("ToolInvoker return req={RequestId} trace={TraceId} tool={Tool} ok=false code=INTERNAL_ERROR ms={Ms}", _ctx.Context?.RequestId, _ctx.Context?.TraceId, toolName, sw.ElapsedMilliseconds);
            return LogAndReturn(EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
                telemetry: EnvelopeTelemetryV1.From(_ctx.Context, sw.ElapsedMilliseconds),
                policy: policy,
                code: "INTERNAL_ERROR",
                message: "Exception while invoking function.",
                details: new { exception = ex.GetType().Name, ex.Message },
                normalizedIntent: normalizedIntent,
                source: source,
                evidence: evidence), sw.ElapsedMilliseconds);
        }
    }

    private object LogAndReturn(EnvelopeV1 envelope, long durationMs)
    {
        var compaction = ToolResultCompactor.CompactEnvelopeWithMetadata(envelope);
        var datasetId = TryGetDatasetId(envelope.NormalizedIntent);

        _logger.LogInformation(
            "ToolExecution audit tool={Tool} ok={Ok} durationMs={DurationMs} compactedBytes={CompactedBytes} truncated={Truncated} outputHash={OutputHash} datasetId={DatasetId} tenantId={TenantId} userId={UserId}",
            envelope.Tool.Name,
            envelope.Ok,
            durationMs,
            compaction.Bytes,
            compaction.Truncated,
            compaction.OutputHash,
            datasetId,
            envelope.Meta.TenantId,
            envelope.Meta.UserId);

        return envelope;
    }

    private sealed record ToolFailure(string Code, string Message, object? Details);

    private static ToolFailure ExtractToolFailure(ToolExecutionResult result)
    {
        var code = "TOOL_EXECUTION_FAILED";
        var message = string.IsNullOrWhiteSpace(result.Message) ? "Tool execution failed." : result.Message;
        var details = result.Data;

        if (TryExtractFailureDetails(result.Data, out var extractedCode, out var extractedMessage))
        {
            if (!string.IsNullOrWhiteSpace(extractedCode))
                code = extractedCode!;

            if (!string.IsNullOrWhiteSpace(extractedMessage))
                message = extractedMessage!;
        }

        return new ToolFailure(code, message, details);
    }

    private static bool TryExtractFailureDetails(object? details, out string? code, out string? message)
    {
        code = null;
        message = null;

        if (details is null)
            return false;

        JsonElement root;
        if (details is JsonElement element)
        {
            root = element;
        }
        else
        {
            try
            {
                root = JsonSerializer.SerializeToElement(details, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }
            catch
            {
                return false;
            }
        }

        if (TryReadError(root, out code, out message))
            return true;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("data", out var dataEl) &&
            TryReadError(dataEl, out code, out message))
        {
            return true;
        }

        return false;
    }

    private static bool TryReadError(JsonElement node, out string? code, out string? message)
    {
        code = null;
        message = null;

        if (node.ValueKind != JsonValueKind.Object)
            return false;

        if (node.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.Object)
        {
            TryGetString(errorEl, "code", out code);
            TryGetString(errorEl, "message", out message);
            if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(message))
                return true;
        }

        TryGetString(node, "code", out code);
        TryGetString(node, "message", out message);
        return !string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(message);
    }

    private static bool TryGetString(JsonElement node, string propertyName, out string? value)
    {
        value = null;
        if (!node.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
            return false;

        value = prop.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? TryGetDatasetId(object? normalizedIntent)
    {
        if (normalizedIntent is DynamicToolIntent dyn)
            return dyn.GetString("datasetId");

        return null;
    }

    private static string? TryGetPayloadKind(object? payload)
    {
        if (payload is null) return null;

        try
        {
            // Best-effort: serialize to a JsonDocument and extract "kind".
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("kind", out var kindEl) && kindEl.ValueKind == JsonValueKind.String
                ? kindEl.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static int? TryGetPayloadSchemaVersion(object? payload)
    {
        if (payload is null) return null;

        try
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("schemaVersion", out var verEl) && verEl.ValueKind == JsonValueKind.Number
                ? verEl.GetInt32()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
