using System.Diagnostics;
using System.Security;
using System.Text.Json;
using System.Text.Json.Nodes;
using TILSOFTAI.Application.Permissions;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Contracts.Evidence;
using TILSOFTAI.Orchestration.Contracts.Validation;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.SK.Conversation;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Filters;

namespace TILSOFTAI.Orchestration.SK;

public sealed class ToolInvoker
{
    private readonly ToolRegistry _registry;
    private readonly ToolDispatcher _dispatcher;
    private readonly RbacService _rbac;
    private readonly ExecutionContextAccessor _ctx;
    private readonly IConversationStateStore _conversationState;
    private readonly IFilterPatchMerger _filterPatchMerger;
    private readonly IResponseSchemaValidator _responseSchemaValidator;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public ToolInvoker(
        ToolRegistry registry,
        ToolDispatcher dispatcher,
        RbacService rbac,
        ExecutionContextAccessor ctx,
        IConversationStateStore conversationState,
        IFilterPatchMerger filterPatchMerger,
        IResponseSchemaValidator responseSchemaValidator)
    {
        _registry = registry;
        _dispatcher = dispatcher;
        _rbac = rbac;
        _ctx = ctx;
        _conversationState = conversationState;
        _filterPatchMerger = filterPatchMerger;
        _responseSchemaValidator = responseSchemaValidator;
    }

    public async Task<object> ExecuteAsync(string toolName, object argsObj, CancellationToken ct)
    {
        // IMPORTANT: Do not throw for expected errors (validation/forbidden/contract).
        // Returning a structured envelope keeps the LLM from retrying the same tool call.

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(argsObj, _json));
        var args = doc.RootElement.Clone();

        // Conversation-aware filter patching (ver19)
        // If the model only specifies delta filters for a follow-up turn, merge with the
        // last successful query's canonical filters.
        args = await MergeConversationFiltersIfNeededAsync(toolName, args, ct);

        var sw = Stopwatch.StartNew();
        var requiresWrite = false;
        object? normalizedIntent = null;

        // Stage 2: envelope policy + telemetry
        var policy = EnvelopePolicyV1.Deny(_ctx.Context, "UNSPECIFIED");
        EnvelopeSourceV1? source = null;
        IReadOnlyList<EnvelopeEvidenceItemV1>? evidence = null;

        object? lastPayload = null;
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

            // Runtime response contract validation (ver25)
            // Ensures tool handlers cannot drift away from governance/contracts.
            lastPayload = dispatchResult.Result.Data;
            _responseSchemaValidator.ValidateOrThrow(lastPayload, toolName);

            // Update conversation state for follow-up turns (only for successful READ queries)
            await TryUpdateConversationStateAsync(toolName, normalizedIntent, requiresWrite, ct);

            // Fallback (anti-loop): if the handler did not attach evidence, synthesize a compact one.
            // Some clients/UI layers render only envelope.evidence; returning an empty list causes
            // the assistant to retry tools and the user sees no answer.
            if (evidence is null || evidence.Count == 0)
                evidence = EvidenceFallbackBuilder.Build(lastPayload);

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
            // This is non-retryable: the payload emitted by the server does not match governance schema.
            // Returning a structured failure envelope prevents the LLM from repeatedly calling the same tool
            // while also keeping the API stable (no hard throw).
            policy = EnvelopePolicyV1.Deny(_ctx.Context, "CONTRACT_ERROR");
            return EnvelopeV1.Failure(toolName, requiresWrite, _ctx.Context,
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

    private async Task<JsonElement> MergeConversationFiltersIfNeededAsync(string toolName, JsonElement args, CancellationToken ct)
    {
        // Only tools that accept a "filters" argument participate.
        if (args.ValueKind != JsonValueKind.Object)
            return args;

        if (!args.TryGetProperty("filters", out var filtersElement))
            return args;

        var patchFilters = ParseFilters(filtersElement);

        // If there is no conversation id, do nothing.
        if (string.IsNullOrWhiteSpace(_ctx.Context.ConversationId))
            return args;

        var state = await _conversationState.TryGetAsync(_ctx.Context, ct);
        var last = state?.LastQuery;
        if (last is null || last.Filters.Count == 0)
            return args;

        // Merge only when the module matches (e.g., models.*)
        if (!IsSameModule(last.Resource, toolName))
            return args;

        var merged = _filterPatchMerger.Merge(toolName, last.Filters, patchFilters).Merged;

        // Rewrite args JSON with merged filters while keeping the rest intact.
        var node = JsonNode.Parse(args.GetRawText()) as JsonObject;
        if (node is null)
            return args;

        node["filters"] = JsonSerializer.SerializeToNode(merged, _json);

        using var doc = JsonDocument.Parse(node.ToJsonString(_json));
        return doc.RootElement.Clone();
    }

    private async Task TryUpdateConversationStateAsync(string toolName, object? normalizedIntent, bool requiresWrite, CancellationToken ct)
    {
        if (requiresWrite)
            return;

        // Only cache dynamic queries that actually carry filters.
        if (normalizedIntent is not DynamicToolIntent dyn)
            return;

        if (dyn.Filters is null)
            return;

        // Canonicalize again defensively by reusing the merger on an empty base.
        var merged = _filterPatchMerger.Merge(toolName,
            baseFilters: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            patchFilters: dyn.Filters).Merged;

        var state = new ConversationState
        {
            LastQuery = new ConversationQueryState
            {
                Resource = toolName,
                ToolName = toolName,
                Filters = merged,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            }
        };

        await _conversationState.UpsertAsync(_ctx.Context, state, ct);
    }

    private static bool IsSameModule(string resourceA, string resourceB)
    {
        var a = (resourceA ?? string.Empty).Split('.', 2)[0];
        var b = (resourceB ?? string.Empty).Split('.', 2)[0];
        return !string.IsNullOrWhiteSpace(a) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> ParseFilters(JsonElement filtersElement)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (filtersElement.ValueKind != JsonValueKind.Object)
            return dict;

        foreach (var p in filtersElement.EnumerateObject())
        {
            dict[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => p.Value.ToString()
            };
        }

        return dict;
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
