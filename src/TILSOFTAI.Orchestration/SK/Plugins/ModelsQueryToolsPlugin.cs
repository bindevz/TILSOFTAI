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
    public Task<object> SearchAsync(Dictionary<string, string?>? filters = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.search", new { filters = filters ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), page, pageSize }, ct);

    [KernelFunction("count")]
    [Description("Đếm tổng số models theo filters.")]
    public Task<object> CountAsync(Dictionary<string, string?>? filters = null, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.count", new { filters = filters ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) }, ct);

    [KernelFunction("stats")]
    [Description("Thống kê models theo season/collection/rangeName. Trả breakdowns + highlights.")]
    public Task<object> StatsAsync(Dictionary<string, string?>? filters = null, int topN = 10, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.stats", new { filters = filters ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase), topN }, ct);

    [KernelFunction("get")]
    [Description("Lấy chi tiết một model theo modelId (GUID).")]
    public Task<object> GetAsync(Guid modelId, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.get", new { modelId }, ct);
}
