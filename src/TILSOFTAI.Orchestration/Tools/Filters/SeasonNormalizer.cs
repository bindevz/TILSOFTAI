public static class SeasonNormalizer
{
    public static string? NormalizeToFull(string? season)
    {
        if (string.IsNullOrWhiteSpace(season)) return null;
        season = season.Trim();

        // full already
        var m1 = System.Text.RegularExpressions.Regex.Match(season, @"\b(20\d{2})\s*[/\-]\s*(20\d{2})\b");
        if (m1.Success) return $"{m1.Groups[1].Value}/{m1.Groups[2].Value}";

        // short 24/25 -> 2024/2025
        var m2 = System.Text.RegularExpressions.Regex.Match(season, @"\b(\d{2})\s*[/\-]\s*(\d{2})\b");
        if (m2.Success)
        {
            var a = int.Parse(m2.Groups[1].Value);
            var b = int.Parse(m2.Groups[2].Value);
            var y1 = 2000 + a;
            var y2 = (b == ((a + 1) % 100)) ? y1 + 1 : (2000 + b);
            if (b < a) y2 += 100;
            return $"{y1}/{y2}";
        }

        return season;
    }
}
