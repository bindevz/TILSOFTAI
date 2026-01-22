using Microsoft.SemanticKernel;
using System.ComponentModel;
using TILSOFTAI.Orchestration.Tools;

namespace TILSOFTAI.Orchestration.SK.Plugins;

[SkModule("common")]
public sealed class FiltersToolsPlugin
{
    private readonly ToolInvoker _invoker;
    public FiltersToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("catalog")]
    [Description("Trả về danh sách filters hợp lệ theo resource (vd: 'atomic.query.execute', 'analytics.run'). Nếu không truyền resource sẽ trả danh sách resources.")]
    public Task<object> CatalogAsync(string? resource = null, bool includeValues = false, CancellationToken ct = default)
        => _invoker.ExecuteAsync("filters.catalog", new { resource, includeValues }, ToolExposurePolicy.ModeBAllowedTools, ct);

    [KernelFunction("actions_catalog")]
    [Description("Trả về danh mục thao tác ghi (prepare/commit) và schema tham số. Dùng khi cần biết cần truyền gì để tạo/cập nhật dữ liệu.")]
    public Task<object> ActionsCatalogAsync(string? action = null, bool includeExamples = false, CancellationToken ct = default)
        => _invoker.ExecuteAsync("actions.catalog", new { action, includeExamples }, ToolExposurePolicy.ModeBAllowedTools, ct);
}

