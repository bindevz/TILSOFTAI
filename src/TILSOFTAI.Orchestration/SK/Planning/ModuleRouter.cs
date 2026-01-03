using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Orchestration.SK.Planning;

/// <summary>
/// Selects which tool modules (plugins) should be exposed to the LLM for a given user message.
/// "Level 2" strategy: expose only a small subset of modules per request.
/// </summary>
public sealed class ModuleRouter
{
    public IReadOnlyCollection<string> SelectModules(string userText, TSExecutionContext context)
    {
        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var t = (userText ?? string.Empty).ToLowerInvariant();

        // Models / Products
        if (ContainsAny(t, "model", "mẫu", "sản phẩm", "sku", "attribute", "giá"))
            modules.Add("models");

        // Analytics/reporting often needs multiple modules
        if (ContainsAny(t, "báo cáo", "phân tích", "doanh số", "lợi nhuận", "kpi", "trend", "xu hướng"))
        {
            modules.Add("models");
        }

        // If nothing matches, expose no tools (LLM will respond naturally per system prompt).
        if (modules.Count == 0)
            return Array.Empty<string>();

        //nếu có ít nhất 1 module business được chọn, thì add thêm common
        modules.Add("common");
        return modules;
    }

    private static bool ContainsAny(string text, params string[] keys)
        => keys.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
