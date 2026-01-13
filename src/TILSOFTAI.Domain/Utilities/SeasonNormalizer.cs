using System.Text.RegularExpressions;

namespace TILSOFTAI.Domain.Utilities;

/// <summary>
/// Normalizes season expressions to a canonical form "YYYY/YYYY".
/// Examples:
/// - "24/25" => "2024/2025"
/// - "2024/25" => "2024/2025"
/// - "25-26" => "2025/2026"
///
/// This utility is language-agnostic and can be applied to both Vietnamese and English inputs.
/// </summary>
public static class SeasonNormalizer
{
    // Matches forms like: 24/25, 24-25, 2024/2025, 2024-25.
    private static readonly Regex SeasonRegex = new(
        @"(?<!\d)(?<y1>\d{2}|\d{4})\s*[/\-]\s*(?<y2>\d{2}|\d{4})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Extracts and normalizes the first season pattern in the text. Returns null if none found.
    /// pivotYear controls 2-digit year mapping: <= pivot -> 20xx, otherwise 19xx.
    /// </summary>
    public static string? NormalizeFromText(string? text, int pivotYear = 50)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var m = SeasonRegex.Match(text);
        if (!m.Success)
            return null;

        var y1 = ParseYear(m.Groups["y1"].Value, pivotYear);
        var y2 = ParseYear(m.Groups["y2"].Value, pivotYear);

        // If user wrote "2024/25", interpret 25 as 2025.
        // Also handle edge where y2 < y1 because of century inference.
        if (y2 < y1 && (y1 - y2) >= 50)
            y2 += 100;

        return $"{y1:D4}/{y2:D4}";
    }

    /// <summary>
    /// Normalizes a season value to canonical form if parseable. Otherwise returns trimmed original.
    /// </summary>
    public static string NormalizeValue(string? seasonValue, int pivotYear = 50)
    {
        if (string.IsNullOrWhiteSpace(seasonValue))
            return string.Empty;

        return NormalizeFromText(seasonValue, pivotYear) ?? seasonValue.Trim();
    }

    private static int ParseYear(string raw, int pivotYear)
    {
        raw = raw.Trim();
        if (raw.Length == 4)
            return int.Parse(raw);

        var yy = int.Parse(raw);
        return yy <= pivotYear ? 2000 + yy : 1900 + yy;
    }
}
