namespace TILSOFTAI.Orchestration.Tools.ToolSchemas;

public enum ToolArgType
{
    String,
    Int,
    Bool,
    Guid,
    Decimal,
    /// <summary>
    /// Arbitrary JSON value (object/array/scalar). Parsed as JsonElement (cloned).
    /// </summary>
    Json,
    /// <summary>
    /// JSON object where values are strings. Parsed as Dictionary&lt;string,string&gt;.
    /// </summary>
    StringMap
}

public sealed record ToolArgSpec(
    string Name,
    ToolArgType Type,
    bool Required = false,
    object? Default = null,
    int? MinInt = null,
    int? MaxInt = null);

public sealed class ToolInputSpec
{
    public required string ToolName { get; set; }
    public bool SupportsPaging { get; set; }
    public int DefaultPage { get; set; } = 1;
    public int DefaultPageSize { get; set; } = 20;
    public int MaxPageSize { get; set; } = 200;

    /// <summary>
    /// Whitelist filter keys for this tool (canonical keys as defined by the tool schema).
    /// Validation will drop unknown keys rather than hard-fail.
    /// </summary>
    public HashSet<string> AllowedFilterKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whitelist scalar arguments at the top-level (outside filters).
    /// </summary>
    public Dictionary<string, ToolArgSpec> Args { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> AllowedArgumentNames
    {
        get
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (AllowedFilterKeys.Count > 0) s.Add("filters");
            if (SupportsPaging)
            {
                s.Add("page");
                s.Add("pageSize");
            }
            foreach (var k in Args.Keys)
                s.Add(k);
            return s;
        }
    }
}
