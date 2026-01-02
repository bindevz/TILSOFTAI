using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace TILSOFTAI.Orchestration.SK.Plugins;

[SkModule("common")]
public sealed class FiltersToolsPlugin
{
    private readonly ToolInvoker _invoker;
    public FiltersToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("catalog")]
    [Description("Trả về danh sách filters hợp lệ theo resource (vd: 'models.search', 'orders.query'). Nếu không truyền resource sẽ trả danh sách resources.")]
    public Task<object> CatalogAsync(string? resource = null, bool includeValues = false, CancellationToken ct = default)
        => _invoker.ExecuteAsync("filters.catalog", new { resource, includeValues }, ct);
}
