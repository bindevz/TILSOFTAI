using TILSOFTAI.Orchestration.SK.Planning;
using TILSOFTAI.Orchestration.SK.Plugins;

namespace TILSOFTAI.Orchestration.Modules.Models;

/// <summary>
/// Reduces tool overload by only exposing model tool packs that match the user message.
/// This is a lightweight heuristic (no LLM call) and can be replaced by an embedding/
/// classifier later.
/// </summary>
public sealed class ModelsPluginExposurePolicy : IPluginExposurePolicy
{
    public bool CanHandle(string module) => string.Equals(module, "models", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyCollection<Type> Select(string module, IReadOnlyCollection<Type> candidates, string lastUserMessage)
    {
        var text = (lastUserMessage ?? string.Empty).ToLowerInvariant();

        bool wantsWrite = ContainsAny(text, "tạo", "thêm", "create", "add", "commit", "xác nhận");
        bool wantsOptions = ContainsAny(text, "option", "tùy chọn", "tuỳ chọn", "thuộc tính", "attribute", "cấu hình", "constraint", "ràng buộc");
        bool wantsPrice = ContainsAny(text, "giá", "price", "cost", "costing", "phân tích giá", "breakdown");

        var selected = new HashSet<Type>();

        // Default pack: query.
        TryAdd<ModelsQueryToolsPlugin>(candidates, selected);

        if (wantsOptions) TryAdd<ModelsOptionsToolsPlugin>(candidates, selected);
        if (wantsPrice) TryAdd<ModelsPriceToolsPlugin>(candidates, selected);
        if (wantsWrite) TryAdd<ModelsWriteToolsPlugin>(candidates, selected);

        // If none matched (e.g., other language), keep all candidates as a safe fallback.
        return selected.Count > 0 ? selected.ToArray() : candidates;
    }

    private static void TryAdd<T>(IReadOnlyCollection<Type> candidates, HashSet<Type> selected)
    {
        var t = typeof(T);
        if (candidates.Contains(t)) selected.Add(t);
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            if (text.Contains(n)) return true;
        }
        return false;
    }
}
