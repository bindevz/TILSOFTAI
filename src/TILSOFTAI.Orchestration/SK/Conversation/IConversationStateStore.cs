using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Orchestration.SK.Conversation;

public interface IConversationStateStore
{
    Task<ConversationState?> TryGetAsync(TSExecutionContext ctx, CancellationToken ct);
    Task UpsertAsync(TSExecutionContext ctx, ConversationState state, CancellationToken ct);
    Task ClearAsync(TSExecutionContext ctx, CancellationToken ct);
}
