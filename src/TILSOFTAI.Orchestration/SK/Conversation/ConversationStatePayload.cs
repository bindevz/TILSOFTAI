using System.Text.Json;

namespace TILSOFTAI.Orchestration.SK.Conversation;

/// <summary>
/// Versioned wrapper for persisted conversation state.
/// Enables safe evolution of the state schema over time.
/// </summary>
public sealed class ConversationStatePayload
{
    public int V { get; set; }
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public required ConversationState State { get; set; }

    public static ConversationStatePayload Wrap(ConversationState state, int version)
        => new() { V = version, SavedAtUtc = DateTimeOffset.UtcNow, State = state };

    public static bool TryDeserialize(string json, int currentVersion, JsonSerializerOptions options, out ConversationStatePayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            // Preferred format: wrapper with V + State
            var wrapper = JsonSerializer.Deserialize<ConversationStatePayload>(json, options);
            if (wrapper?.State != null)
            {
                // If version is newer than the running code, treat as incompatible.
                if (wrapper.V > currentVersion)
                    return false;

                payload = wrapper;
                return true;
            }
        }
        catch
        {
            // ignore and try legacy format below
        }

        try
        {
            // Legacy format: raw ConversationState without wrapper (v0)
            var legacy = JsonSerializer.Deserialize<ConversationState>(json, options);
            if (legacy != null)
            {
                payload = Wrap(legacy, 0);
                return true;
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }
}
