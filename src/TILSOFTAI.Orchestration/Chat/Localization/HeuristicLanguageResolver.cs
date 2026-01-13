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

    private static readonly Regex ViSignals = new(
        // Vietnamese signals (including no-diacritic variants). Avoid English-only tokens like "season"/"collection"
        // to prevent misclassification when the user asks in English.
        @"(?i)\b(bao\s*nhieu|bao\s*nhieu\?|dem|đếm|so\s*luong|so\s*luong\s*bao\s*nhieu|tong\s*so|mua|mùa|bo\s*suu\s*tap|bộ\s*sưu\s*tập|khach\s*hang|khách\s*hàng|don\s*hang|đơn\s*hàng|gia|giá|tim|tìm|liet\s*ke|liệt\s*kê|danh\s*sach|danh\s*sách)\b",
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
            if (ViSignals.IsMatch(lastUser)) return ChatLanguage.Vi;
            if (EnSignals.IsMatch(lastUser)) return ChatLanguage.En;
        }

        // Fall back to stored preference for the conversation.
        if (!string.IsNullOrWhiteSpace(conversationState?.PreferredLanguage))
        {
            return ChatLanguageExtensions.FromIsoCode(conversationState.PreferredLanguage);
        }

        // Default to English.
        return ChatLanguage.En;
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
