using System.Text.RegularExpressions;

namespace TILSOFTAI.Domain.ValueObjects;

public sealed record SeasonCode(string Value)
{
    private static readonly Regex ShortPattern = new(@"^(?<first>\d{2})[\/\-](?<second>\d{2})$", RegexOptions.Compiled);
    private static readonly Regex LongPattern = new(@"^(?<first>\d{4})[\/\-](?<second>\d{4})$", RegexOptions.Compiled);

    public static SeasonCode Parse(string input)
    {
        var normalizedInput = input.Trim();

        var shortMatch = ShortPattern.Match(normalizedInput);
        if (shortMatch.Success)
        {
            var first = int.Parse(shortMatch.Groups["first"].Value);
            var second = int.Parse(shortMatch.Groups["second"].Value);
            return new SeasonCode($"{2000 + first}-{2000 + second}");
        }

        var longMatch = LongPattern.Match(normalizedInput);
        if (longMatch.Success)
        {
            var first = int.Parse(longMatch.Groups["first"].Value);
            var second = int.Parse(longMatch.Groups["second"].Value);
            return new SeasonCode($"{first}-{second}");
        }

        throw new ArgumentException("Unsupported season format.", nameof(input));
    }
}
