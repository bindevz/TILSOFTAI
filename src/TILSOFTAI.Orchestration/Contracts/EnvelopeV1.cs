using System.Text.Json.Serialization;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Orchestration.Contracts;

/// <summary>
/// Enterprise envelope contract (v1) used consistently for ALL tool outputs.
///
/// Design goals:
/// - Stable top-level fields so the LLM can reliably parse tool evidence.
/// - Works for both success and failure without throwing (prevents tool-call retry loops).
/// - Minimal but sufficient metadata for audit/correlation.
/// </summary>
public sealed record EnvelopeV1
{
    [JsonPropertyName("kind")] public string Kind { get; init; } = "tilsoft.envelope.v1";
    // Non-breaking evolution of the same envelope kind.
    // Stage 2 adds telemetry, policy, source, and evidence registry.
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; init; } = 2;
    [JsonPropertyName("generatedAtUtc")] public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("tool")] public EnvelopeToolV1 Tool { get; init; } = default!;
    [JsonPropertyName("ok")] public bool Ok { get; init; }
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;

    // The normalized intent after validation/canonicalization (best-effort).
    [JsonPropertyName("normalizedIntent")] public object? NormalizedIntent { get; init; }

    // Tool-specific payload. This should itself follow a tool contract (e.g. models.stats.v1)
    // but is always accessible at envelope.data.
    [JsonPropertyName("data")] public object Data { get; init; } = new { };

    [JsonPropertyName("warnings")] public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    [JsonPropertyName("error")] public EnvelopeErrorV1? Error { get; init; }
    [JsonPropertyName("meta")] public EnvelopeMetaV1 Meta { get; init; } = default!;

    // Enterprise stage 2 fields
    [JsonPropertyName("telemetry")] public EnvelopeTelemetryV1 Telemetry { get; init; } = default!;
    [JsonPropertyName("policy")] public EnvelopePolicyV1 Policy { get; init; } = default!;
    [JsonPropertyName("source")] public EnvelopeSourceV1? Source { get; init; }
    [JsonPropertyName("evidence")] public IReadOnlyList<EnvelopeEvidenceItemV1> Evidence { get; init; } = Array.Empty<EnvelopeEvidenceItemV1>();

    public static EnvelopeV1 Success(
        string toolName,
        bool requiresWrite,
        TSExecutionContext ctx,
        EnvelopeTelemetryV1 telemetry,
        EnvelopePolicyV1 policy,
        object? normalizedIntent,
        string message,
        object data,
        IReadOnlyList<string>? warnings = null,
        EnvelopeSourceV1? source = null,
        IReadOnlyList<EnvelopeEvidenceItemV1>? evidence = null)
        => new()
        {
            Ok = true,
            Tool = new EnvelopeToolV1
            {
                Name = toolName,
                RequiresWrite = requiresWrite
            },
            Meta = EnvelopeMetaV1.From(ctx),
            Telemetry = telemetry,
            Policy = policy,
            Source = source,
            Evidence = evidence ?? Array.Empty<EnvelopeEvidenceItemV1>(),
            NormalizedIntent = normalizedIntent,
            Message = message,
            Data = data ?? new { },
            Warnings = warnings ?? Array.Empty<string>(),
            Error = null
        };

    public static EnvelopeV1 Failure(
        string toolName,
        bool requiresWrite,
        TSExecutionContext ctx,
        EnvelopeTelemetryV1 telemetry,
        EnvelopePolicyV1 policy,
        string code,
        string message,
        object? details = null,
        object? normalizedIntent = null,
        EnvelopeSourceV1? source = null,
        IReadOnlyList<EnvelopeEvidenceItemV1>? evidence = null)
        => new()
        {
            Ok = false,
            Tool = new EnvelopeToolV1
            {
                Name = toolName,
                RequiresWrite = requiresWrite
            },
            Meta = EnvelopeMetaV1.From(ctx),
            Telemetry = telemetry,
            Policy = policy,
            Source = source,
            Evidence = evidence ?? Array.Empty<EnvelopeEvidenceItemV1>(),
            NormalizedIntent = normalizedIntent,
            Message = message,
            Data = new { },
            Warnings = Array.Empty<string>(),
            Error = new EnvelopeErrorV1
            {
                Code = code,
                Message = message,
                Details = details
            }
        };
}

public sealed record EnvelopeToolV1
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("requiresWrite")] public bool RequiresWrite { get; init; }
}

public sealed record EnvelopeErrorV1
{
    [JsonPropertyName("code")] public string Code { get; init; } = string.Empty;
    [JsonPropertyName("message")] public string Message { get; init; } = string.Empty;
    [JsonPropertyName("details")] public object? Details { get; init; }
}

public sealed record EnvelopeMetaV1
{
    [JsonPropertyName("tenantId")] public string TenantId { get; init; } = string.Empty;
    [JsonPropertyName("userId")] public string UserId { get; init; } = string.Empty;
    [JsonPropertyName("correlationId")] public string CorrelationId { get; init; } = string.Empty;
    [JsonPropertyName("roles")] public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();

    public static EnvelopeMetaV1 From(TSExecutionContext ctx)
        => new()
        {
            TenantId = ctx.TenantId,
            UserId = ctx.UserId,
            CorrelationId = ctx.CorrelationId,
            Roles = ctx.Roles.ToArray()
        };
}

public sealed record EnvelopeTelemetryV1
{
    [JsonPropertyName("requestId")] public string RequestId { get; init; } = string.Empty;
    [JsonPropertyName("traceId")] public string TraceId { get; init; } = string.Empty;
    [JsonPropertyName("durationMs")] public long DurationMs { get; init; }

    public static EnvelopeTelemetryV1 From(TSExecutionContext ctx, long durationMs)
        => new()
        {
            RequestId = ctx.RequestId,
            TraceId = ctx.TraceId,
            DurationMs = durationMs
        };
}

public sealed record EnvelopePolicyV1
{
    [JsonPropertyName("decision")] public string Decision { get; init; } = "allow"; // allow|deny
    [JsonPropertyName("reasonCode")] public string ReasonCode { get; init; } = string.Empty;
    [JsonPropertyName("checkedAtUtc")] public DateTimeOffset CheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    [JsonPropertyName("rolesEvaluated")] public IReadOnlyList<string> RolesEvaluated { get; init; } = Array.Empty<string>();

    public static EnvelopePolicyV1 Allow(TSExecutionContext ctx)
        => new() { Decision = "allow", ReasonCode = "OK", RolesEvaluated = ctx.Roles.ToArray() };

    public static EnvelopePolicyV1 Deny(TSExecutionContext ctx, string reasonCode)
        => new() { Decision = "deny", ReasonCode = reasonCode, RolesEvaluated = ctx.Roles.ToArray() };
}

public sealed record EnvelopeSourceV1
{
    [JsonPropertyName("system")] public string System { get; init; } = string.Empty; // sqlserver|registry|memory
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;     // stored procedure / module
    [JsonPropertyName("cache")] public string? Cache { get; init; }                 // hit|miss|na
    [JsonPropertyName("note")] public string? Note { get; init; }
}

public sealed record EnvelopeEvidenceItemV1
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty; // metric|breakdown|list|entity
    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;
    [JsonPropertyName("payload")] public object Payload { get; init; } = new { };
}
