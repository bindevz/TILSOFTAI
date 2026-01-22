namespace TILSOFTAI.Orchestration.Tools.Modularity;

public interface IFilterCanonicalizer
{
    (Dictionary<string, string?> Applied, string[] Rejected) Canonicalize(string resource, IReadOnlyDictionary<string, string?> incoming);
}

public sealed class FilterCanonicalizer : IFilterCanonicalizer
{
    public (Dictionary<string, string?> Applied, string[] Rejected) Canonicalize(string resource, IReadOnlyDictionary<string, string?> incoming)
    {
        if (incoming.Count == 0)
            return (new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), Array.Empty<string>());

        var applied = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in incoming)
        {
            if (string.IsNullOrWhiteSpace(k))
                continue;

            applied[k.Trim()] = v?.Trim();
        }

        return (applied, Array.Empty<string>());
    }
}
