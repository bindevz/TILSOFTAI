using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Orchestration.SK.Conversation;

/// <summary>
/// Conversation state is used to make follow-up questions deterministic.
/// We keep the last successful READ query (tool + canonical filters) so
/// the next user turn can patch/override a subset of filters without losing
/// context (e.g., keep collection=Outdoor, only change season).
/// </summary>
public sealed class ConversationState
{
    public ConversationQueryState? LastQuery { get; set; }

    /// <summary>
    /// Preferred response language for this conversation ("vi" or "en").
    /// This is used for short follow-ups that omit language cues.
    /// </summary>
    public string? PreferredLanguage { get; set; }
}

public sealed class ConversationQueryState
{
    /// <summary>
    /// Canonical resource name used by filters-catalog (typically same as tool name, e.g., "models.count").
    /// </summary>
    public required string Resource { get; init; }

    /// <summary>
    /// Tool name invoked (e.g., "models.count").
    /// </summary>
    public required string ToolName { get; init; }

    /// <summary>
    /// Canonical, catalog-validated filters.
    /// Keys MUST be canonical keys from filters-catalog for the given resource.
    /// </summary>
    public Dictionary<string, string?> Filters { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal static class ConversationStateKeys
{
    public static string BuildKey(TSExecutionContext ctx)
        => $"{ctx.TenantId}::{ctx.UserId}::{ctx.ConversationId}";
}
