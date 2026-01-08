namespace TILSOFTAI.Orchestration.SK.Conversation;

/// <summary>
/// Configuration for conversation state persistence.
///
/// Default provider is <c>InMemory</c> for local development.
/// Set <c>Provider=Redis</c> and <c>Redis.ConnectionString</c> to enable distributed storage.
/// </summary>
public sealed class ConversationStateStoreOptions
{
    /// <summary>
    /// Provider name. Supported: "InMemory", "Redis".
    /// </summary>
    public string Provider { get; set; } = "InMemory";

    /// <summary>
    /// Time-to-live for a conversation state record.
    /// </summary>
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// If true, each read refreshes expiry (sliding TTL).
    /// </summary>
    public bool SlidingTtlEnabled { get; set; } = true;

    /// <summary>
    /// Payload version used when serializing conversation state to external storage.
    /// </summary>
    public int PayloadVersion { get; set; } = 1;

    /// <summary>
    /// Prefix applied to the generated storage key.
    /// </summary>
    public string KeyPrefix { get; set; } = "tilsoftai:conv:";

    public RedisOptions Redis { get; set; } = new();

    public sealed class RedisOptions
    {
        /// <summary>
        /// StackExchange.Redis connection string.
        /// Example: "192.168.8.7:6379,abortConnect=false".
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>
        /// Redis database index. Use -1 for default.
        /// </summary>
        public int Database { get; set; } = -1;
    }
}
