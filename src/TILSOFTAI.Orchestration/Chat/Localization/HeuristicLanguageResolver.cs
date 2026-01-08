using System.Text.RegularExpressions;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.SK.Conversation;

namespace TILSOFTAI.Orchestration.Chat.Localization;

/// <summary>
/// Lightweight language detection suitable for ERP chat.
/// Goals: stable, fast, no external dependencies.
/// </summary>
public sealed class HeuristicLanguageResolver : ILanguageResolver
{
    private static readonly Regex EnSignals = new(
        @"(?i)\b(how many|what|which|show|list|count|season|collection|model|customer|order|price|in the system|with)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public ChatLanguage Resolve(IReadOnlyCollection<ChatCompletionMessage> incomingMessages, ConversationState? conversationState)
    {
        // Use the last non-empty user message as primary signal.
        var lastUser = incomingMessages.LastOrDefault(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(m.Content))?.Content;

        if (!string.IsNullOrWhiteSpace(lastUser))
        {
            if (ContainsVietnameseDiacritics(lastUser)) return ChatLanguage.Vi;
            if (EnSignals.IsMatch(lastUser)) return ChatLanguage.En;
        }

        // Fall back to stored preference for the conversation.
        if (!string.IsNullOrWhiteSpace(conversationState?.PreferredLanguage))
        {
            return ChatLanguageExtensions.FromIsoCode(conversationState.PreferredLanguage);
        }

        // Default to Vietnamese.
        return ChatLanguage.Vi;
    }

    private static bool ContainsVietnameseDiacritics(string text)
    {
        // Common Vietnamese-specific letters and combining patterns.
        // This is a fast heuristic; it is intentionally not exhaustive.
        foreach (var ch in text)
        {
            if (ch is 'đ' or 'Đ' or 'ă' or 'Ă' or 'â' or 'Â' or 'ê' or 'Ê' or 'ô' or 'Ô' or 'ơ' or 'Ơ' or 'ư' or 'Ư')
                return true;

            // Combining diacritics block (covers most accented Latin characters)
            if (ch >= '\u0300' && ch <= '\u036F')
                return true;
        }

        return false;
    }
}
