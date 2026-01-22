
namespace TILSOFTAI.Orchestration.Tools;

/// <summary>
/// Enterprise intent for most READ/list/count/stats tools.
/// - Filters: dynamic key/value filters from LLM.
/// - Page/PageSize: paging (when tool supports it).
/// - Args: strongly typed scalar arguments (topN, modelId, includeValues, ...), validated by tool spec.
/// </summary>
public sealed record DynamicToolIntent(
    IReadOnlyDictionary<string, string?> Filters,
    int Page,
    int PageSize,
    IReadOnlyDictionary<string, object?> Args);

// Stage 2 (Enterprise): All tools, including WRITE tools, reuse the same DynamicToolIntent.
// Write tools are still governed by:
// - RBAC (application layer)
// - 2-step confirmation plans (prepare -> commit)
