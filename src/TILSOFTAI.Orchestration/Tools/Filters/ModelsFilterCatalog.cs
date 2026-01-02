public static class ModelsFilterCatalog
{
    // Canonical keys
    public const string Season = "season";
    public const string Collection = "collection";
    public const string RangeName = "rangeName";
    public const string ModelCode = "modelCode";
    public const string ModelName = "modelName";

    // Aliases (LLM hay dùng)
    private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["category"] = RangeName,
        ["name"] = ModelName,
        ["range"] = RangeName,
        ["code"] = ModelCode,
    };

    public static string NormalizeKey(string key)
        => _aliases.TryGetValue(key, out var k) ? k : key;

    public static bool IsSupported(string key)
        => key.Equals(Season, StringComparison.OrdinalIgnoreCase)
        || key.Equals(Collection, StringComparison.OrdinalIgnoreCase)
        || key.Equals(RangeName, StringComparison.OrdinalIgnoreCase)
        || key.Equals(ModelCode, StringComparison.OrdinalIgnoreCase)
        || key.Equals(ModelName, StringComparison.OrdinalIgnoreCase);
}

