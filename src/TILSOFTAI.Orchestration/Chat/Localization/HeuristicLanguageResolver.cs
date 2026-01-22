using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using TILSOFTAI.Configuration;

namespace TILSOFTAI.Orchestration.Chat.Localization;

/// <summary>
/// Lightweight language detection suitable for ERP chat.
/// Goals: stable, fast, no external dependencies.
/// </summary>
public sealed class HeuristicLanguageResolver : ILanguageResolver
{
    private const string DefaultEnglishCulture = "en-US";
    private const string DefaultVietnameseCulture = "vi-VN";

    private readonly string _defaultCulture;

    private static readonly Regex EnSignals = new(
        @"(?i)\b(how many|what|which|show|list|count|season|collection|model|customer|order|price|in the system|with)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ViSignals = new(
        // Vietnamese signals (including no-diacritic variants). Avoid English-only tokens like "season"/"collection"
        // to prevent misclassification when the user asks in English.
        @"(?i)\b(bao\s*nhieu|dem|so\s*luong|tong\s*so|mua|bo\s*suu\s*tap|khach\s*hang|don\s*hang|gia|tim|liet\s*ke|danh\s*sach)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public HeuristicLanguageResolver(IOptions<AppSettings> settings)
    {
        _defaultCulture = NormalizeCulture(settings.Value.Localization.DefaultCulture);
    }

    public string Resolve(IReadOnlyCollection<ChatCompletionMessage> incomingMessages)
    {
        // Use the last non-empty user message as primary signal.
        var lastUser = incomingMessages.LastOrDefault(m =>
            string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(m.Content))?.Content;

        if (!string.IsNullOrWhiteSpace(lastUser))
        {
            if (ContainsVietnameseDiacritics(lastUser)) return DefaultVietnameseCulture;
            if (ViSignals.IsMatch(lastUser)) return DefaultVietnameseCulture;
            if (EnSignals.IsMatch(lastUser)) return DefaultEnglishCulture;
        }

        // Deterministic fallback from settings.
        return _defaultCulture;
    }

    private static bool ContainsVietnameseDiacritics(string text)
    {
        foreach (var ch in text)
        {
            if (ch is '\u0111' or '\u0110')
                return true;

            // Combining diacritics block (covers most accented Latin characters)
            if (ch >= '\u0300' && ch <= '\u036F')
                return true;

            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
                return true;
        }

        return false;
    }

    private static string NormalizeCulture(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return DefaultEnglishCulture;

        var trimmed = input.Trim();
        if (string.Equals(trimmed, "vi", StringComparison.OrdinalIgnoreCase))
            return DefaultVietnameseCulture;
        if (string.Equals(trimmed, "en", StringComparison.OrdinalIgnoreCase))
            return DefaultEnglishCulture;

        try
        {
            return CultureInfo.GetCultureInfo(trimmed).Name;
        }
        catch (CultureNotFoundException)
        {
            return DefaultEnglishCulture;
        }
    }
}
