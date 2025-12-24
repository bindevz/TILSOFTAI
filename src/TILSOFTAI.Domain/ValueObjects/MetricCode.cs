namespace TILSOFTAI.Domain.ValueObjects;

public sealed record MetricCode(string Value)
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PI"] = "PERFORMANCE_INDEX",
        ["PERFORMANCEINDEX"] = "PERFORMANCE_INDEX",
        ["PERFORMANCE_INDEX"] = "PERFORMANCE_INDEX"
    };

    public static MetricCode Parse(string input)
    {
        if (Map.TryGetValue(input.Trim(), out var value))
        {
            return new MetricCode(value);
        }

        throw new ArgumentException("Unsupported metric.", nameof(input));
    }
}
