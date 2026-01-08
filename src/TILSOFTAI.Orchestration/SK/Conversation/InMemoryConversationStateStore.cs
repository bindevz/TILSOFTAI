using System.Collections.Concurrent;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Orchestration.SK.Conversation;

/// <summary>
/// Lightweight in-memory conversation state store.
/// Suitable for single-node development and LM Studio local runs.
/// For multi-node production deployments, replace with a distributed store (Redis, etc.).
/// </summary>
public sealed class InMemoryConversationStateStore : IConversationStateStore
{
    private sealed class Entry
    {
        public required ConversationState State { get; init; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
    }

    private readonly ConcurrentDictionary<string, Entry> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl;
    private readonly bool _slidingEnabled;

    public InMemoryConversationStateStore() : this(TimeSpan.FromMinutes(30), slidingEnabled: true) { }

    public InMemoryConversationStateStore(TimeSpan ttl, bool slidingEnabled)
    {
        _ttl = ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(30) : ttl;
        _slidingEnabled = slidingEnabled;
    }

    public InMemoryConversationStateStore(ConversationStateStoreOptions options)
        : this(options.Ttl, options.SlidingTtlEnabled)
    {
    }

    public Task<ConversationState?> TryGetAsync(TSExecutionContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = ConversationStateKeys.BuildKey(ctx);

        if (!_store.TryGetValue(key, out var entry))
            return Task.FromResult<ConversationState?>(null);

        if (DateTimeOffset.UtcNow >= entry.ExpiresAtUtc)
        {
            _store.TryRemove(key, out _);
            return Task.FromResult<ConversationState?>(null);
        }

        // Sliding expiry
        if (_slidingEnabled)
            entry.ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_ttl);
        return Task.FromResult<ConversationState?>(entry.State);
    }

    public Task UpsertAsync(TSExecutionContext ctx, ConversationState state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = ConversationStateKeys.BuildKey(ctx);

        var entry = new Entry
        {
            State = state,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(_ttl)
        };

        _store.AddOrUpdate(key, entry, (_, __) => entry);
        return Task.CompletedTask;
    }

    public Task ClearAsync(TSExecutionContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = ConversationStateKeys.BuildKey(ctx);
        _store.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
