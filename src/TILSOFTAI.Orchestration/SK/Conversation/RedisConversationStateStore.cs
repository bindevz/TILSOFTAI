using System.Text.Json;
using StackExchange.Redis;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Orchestration.SK.Conversation;

/// <summary>
/// Redis-backed conversation state store.
/// Designed for multi-node production deployments.
///
/// Features:
/// - Sliding TTL (configurable)
/// - Versioned payload for safe schema evolution
/// </summary>
public sealed class RedisConversationStateStore : IConversationStateStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly ConversationStateStoreOptions _options;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public RedisConversationStateStore(IConnectionMultiplexer redis, ConversationStateStoreOptions options)
    {
        _redis = redis;
        _options = options;
    }

    private IDatabase Db
        => _options.Redis.Database >= 0
            ? _redis.GetDatabase(_options.Redis.Database)
            : _redis.GetDatabase();

    private string BuildKey(TSExecutionContext ctx)
        => _options.KeyPrefix + ConversationStateKeys.BuildKey(ctx);

    public async Task<ConversationState?> TryGetAsync(TSExecutionContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var key = BuildKey(ctx);
        var value = await Db.StringGetAsync(key).ConfigureAwait(false);
        if (value.IsNullOrEmpty)
            return null;

        if (!ConversationStatePayload.TryDeserialize(value!, _options.PayloadVersion, _json, out var payload) || payload?.State == null)
            return null;

        if (_options.SlidingTtlEnabled)
            _ = Db.KeyExpireAsync(key, _options.Ttl);

        return payload.State;
    }

    public async Task UpsertAsync(TSExecutionContext ctx, ConversationState state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var key = BuildKey(ctx);
        var payload = ConversationStatePayload.Wrap(state, _options.PayloadVersion);
        var json = JsonSerializer.Serialize(payload, _json);

        await Db.StringSetAsync(key, json, _options.Ttl).ConfigureAwait(false);
    }

    public async Task ClearAsync(TSExecutionContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var key = BuildKey(ctx);
        await Db.KeyDeleteAsync(key).ConfigureAwait(false);
    }
}
