namespace TILSOFTAI.Orchestration.Tools;

public static class ToolExposurePolicy
{
    public static readonly IReadOnlySet<string> AllowedTools =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "atomic.catalog.search",
            "atomic.query.execute",
            "analytics.run"
        };
}
