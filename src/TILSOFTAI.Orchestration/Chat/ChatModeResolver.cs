using System.Collections.Generic;
using System.Linq;

namespace TILSOFTAI.Orchestration.Chat;

public sealed class ChatModeResolver
{
    private const double Threshold = 0.65;
    private static readonly HashSet<string> AllowedModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "tilsoftai-orchestrator",
        "tilsoftai-conversation"
    };

    public ChatMode Resolve(string? model, string userContent, TILSOFTAI.Orchestration.Llm.ProbeResult probeResult)
    {
        if (!IsSupportedModel(model))
        {
            throw new InvalidOperationException("Unknown model.");
        }

        if (string.IsNullOrWhiteSpace(userContent))
        {
            return ChatMode.Intent;
        }

        var forcedIntent = userContent.Length > 400 || ContainsKeywords(userContent);
        if (forcedIntent)
        {
            return ChatMode.Intent;
        }

        if (!IsKnownModel(model))
        {
            return ChatMode.Intent;
        }

        if (string.Equals(model, "tilsoftai-orchestrator", StringComparison.OrdinalIgnoreCase))
        {
            return ChatMode.Intent;
        }

        if (string.Equals(model, "tilsoftai-conversation", StringComparison.OrdinalIgnoreCase))
        {
            return ChatMode.Conversation;
        }

        if (string.Equals(probeResult.Mode, "erp_intent", StringComparison.OrdinalIgnoreCase) && probeResult.Confidence >= Threshold)
        {
            return ChatMode.Intent;
        }

        if (string.Equals(probeResult.Mode, "conversation", StringComparison.OrdinalIgnoreCase) && probeResult.Confidence >= Threshold)
        {
            return ChatMode.Conversation;
        }

        return ChatMode.Intent;
    }

    public bool IsSupportedModel(string? model) => string.IsNullOrWhiteSpace(model) || AllowedModels.Contains(model);

    private static bool ContainsKeywords(string text)
    {
        var keywords = new[] { "order", "customer", "email", "model", "price", "update", "query", "summary" };
        return keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsKnownModel(string? model) => string.IsNullOrWhiteSpace(model) || AllowedModels.Contains(model);
}

public enum ChatMode
{
    Intent,
    Conversation
}
