using Microsoft.SemanticKernel;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace TILSOFTAI.Orchestration.SK.Plugins;

[SkModule("models")]
public sealed class ModelsQueryToolsPlugin
{
    private readonly ToolInvoker _invoker;

    public ModelsQueryToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("search")]
    [Description("Tìm kiếm models theo filters. Hỗ trợ paging. Trả về ModelID/ModelUD/ModelNM...")]
    public Task<object> SearchAsync(
        [Description("Bộ lọc (dictionary key/value). Keys phải lấy từ filters-catalog với resource='models'. Values là string (ví dụ season='23/24').")]
        Dictionary<string, string?>? filters = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.search", new { filters = filters ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), page, pageSize }, ct);

    [KernelFunction("count")]
    [Description("Đếm tổng số models theo filters.")]
    public Task<object> CountAsync(
        [Description("Bộ lọc (dictionary key/value). Keys phải lấy từ filters-catalog với resource='models'. Values là string (ví dụ season='23/24').")]
        Dictionary<string, string?>? filters = null,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.count", new { filters = filters ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) }, ct);

    [KernelFunction("stats")]
    [Description("Thống kê models theo season/collection/rangeName. Trả breakdowns + highlights.")]
    public Task<object> StatsAsync(
        [Description("Bộ lọc (dictionary key/value). Keys phải lấy từ filters-catalog với resource='models'. Values là string.")]
        Dictionary<string, string?>? filters = null,
        int topN = 10,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.stats", new { filters = filters ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), topN }, ct);

    [KernelFunction("get")]
    [Description("Lấy chi tiết một model theo modelId (int, lấy từ models.search/model.options).")]
    public Task<object> GetAsync(int modelId, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.get", new { modelId }, ct);
}
