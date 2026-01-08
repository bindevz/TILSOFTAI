using System.Text.RegularExpressions;

namespace TILSOFTAI.Orchestration.Chat.Localization;

/// <summary>
/// Centralized, multilingual regex patterns used by the chat pipeline.
/// Keep these patterns in code (not in prompts) to reduce prompt dependence.
/// </summary>
public sealed class ChatTextPatterns
{
    public Regex SeasonCode { get; } = new(@"\b\d{2}\s*/\s*\d{2}\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Regex FollowUpStarters { get; } = new(@"(?i)^(mùa|season|còn|thế|vậy|tiếp|ok|đổi|thay|and|then|so|what\s+about|how\s+about|same\s+for)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Regex ResetFilters { get; } = new(@"(?i)\b(bỏ\s*hết|xoá\s*hết|xóa\s*hết|xóa\s*bộ\s*lọc|xoá\s*bộ\s*lọc|clear\s*filters|reset\s*filters|reset\s+all\s+filters|start\s+over|clear\s+all)\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public Regex ConfirmationId { get; } = new(@"(?i)\b(xác\s*nhận|confirm)\b[^a-f0-9]*([a-f0-9]{32})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public bool IsLikelyFollowUp(string text)
    {
        var t = (text ?? string.Empty).Trim();
        if (t.Length == 0) return false;

        // Short turns are usually follow-ups.
        if (t.Length <= 24) return true;

        if (SeasonCode.IsMatch(t)) return true;
        if (FollowUpStarters.IsMatch(t)) return true;

        return false;
    }

    public bool IsResetFiltersIntent(string text)
        => ResetFilters.IsMatch((text ?? string.Empty).Trim());

    public string? TryExtractConfirmationId(string text)
    {
        var m = ConfirmationId.Match(text ?? string.Empty);
        return m.Success ? m.Groups[2].Value : null;
    }
}
