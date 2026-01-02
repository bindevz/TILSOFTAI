using System.Text.RegularExpressions;

namespace TILSOFTAI.Orchestration.Tools.Filters;

/// <summary>
/// Normalizes season inputs (e.g. "24/25") to a stable, database-friendly canonical form (e.g. "2024/2025").
/// </summary>
public static class SeasonNormalizer
{
    public static string? NormalizeToFull(string? season)
    {
        if (string.IsNullOrWhiteSpace(season)) return null;
        season = season.Trim();

        // Full already: 2024/2025 or 2024-2025
        var m1 = Regex.Match(season, @"\b(20\d{2})\s*[/\-]\s*(20\d{2})\b");
        if (m1.Success) return $"{m1.Groups[1].Value}/{m1.Groups[2].Value}";

        // Short: 24/25 -> 2024/2025 ; 99/00 -> 2099/2100 (rare but handled)
        var m2 = Regex.Match(season, @"\b(\d{2})\s*[/\-]\s*(\d{2})\b");
        if (m2.Success)
        {
            var a = int.Parse(m2.Groups[1].Value);
            var b = int.Parse(m2.Groups[2].Value);
            var y1 = 2000 + a;
            var y2 = 2000 + b;

            // If looks like a standard consecutive season, prefer y1+1
            if (b == ((a + 1) % 100))
            {
                y2 = y1 + 1;
            }

            // Handle century rollover like 99/00
            if (b < a)
            {
                y2 += 100;
            }

            return $"{y1}/{y2}";
        }

        return season;
    }
}
