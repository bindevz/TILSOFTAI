using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace TILSOFTAI.Orchestration.SK.Plugins;

[SkModule("analytics")]
public sealed class AnalyticsToolsPlugin
{
    private readonly ToolInvoker _invoker;

    public AnalyticsToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("dataset_create")]
    [Description("Tạo dataset thô phía server (Atomic Data Engine). Trả về datasetId + schema + preview nhỏ.")]
    public Task<object> CreateDatasetAsync(
        [Description("Nguồn dữ liệu. Ver23 hỗ trợ: 'models'.")]
        string source = "models",
        [Description("Bộ lọc (dictionary key/value). Keys phải lấy từ filters-catalog với resource='analytics.dataset.create'.")]
        Dictionary<string, string?>? filters = null,
        [Description("Danh sách cột cần lấy (JSON array of strings). Nếu bỏ trống sẽ lấy default.")]
        JsonElement? select = null,
        int maxRows = 20000,
        int maxColumns = 40,
        int previewRows = 100,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("analytics.dataset.create", new
        {
            source,
            filters = filters ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase),
            select,
            maxRows,
            maxColumns,
            previewRows
        }, ct);

    [KernelFunction("run")]
    [Description("Chạy pipeline phân tích (groupBy/sort/topN/select/filter) trên datasetId.")]
    public Task<object> RunAsync(
        string datasetId,
        [Description("Pipeline JSON array. Mỗi step là object với 'op' (filter/groupBy/sort/topN/select).")]
        JsonElement pipeline,
        int topN = 20,
        int maxGroups = 200,
        int maxResultRows = 500,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("analytics.run", new { datasetId, pipeline, topN, maxGroups, maxResultRows }, ct);
}
