using System.ComponentModel;
using Microsoft.SemanticKernel;
using TILSOFTAI.Orchestration.SK;

namespace TILSOFTAI.Orchestration.SK.Plugins;

[SkModule("orders")]
public sealed class OrdersToolsPlugin
{
    private readonly ToolInvoker _invoker;

    public OrdersToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("query")]
    [Description("Truy vấn đơn hàng theo bộ lọc động. filters có thể gồm: customerId, status, startDate, endDate. (Thiếu date sẽ mặc định 90 ngày gần nhất.)")]
    public Task<object> QueryAsync(
        Dictionary<string, string?>? filters = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("orders.query", new { filters, page, pageSize }, ct);

    [KernelFunction("summary")]
    [Description("Tổng hợp đơn hàng theo bộ lọc động. filters có thể gồm: customerId, status, startDate, endDate. (Thiếu date sẽ mặc định 90 ngày gần nhất.)")]
    public Task<object> SummaryAsync(
        Dictionary<string, string?>? filters = null,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("orders.summary", new { filters }, ct);

    [KernelFunction("create_prepare")]
    [Description("Chuẩn bị tạo đơn hàng. KHÔNG commit. Trả confirmationId + preview.")]
    public Task<object> CreatePrepareAsync(Guid customerId, Guid modelId, string? color = null, int quantity = 1, CancellationToken ct = default)
        => _invoker.ExecuteAsync("orders.create.prepare", new { customerId, modelId, color, quantity }, ct);

    [KernelFunction("create_commit")]
    [Description("Commit tạo đơn hàng sau khi user xác nhận. Yêu cầu confirmationId.")]
    public Task<object> CreateCommitAsync(string confirmationId, CancellationToken ct = default)
        => _invoker.ExecuteAsync("orders.create.commit", new { confirmationId }, ct);
}
