using System.ComponentModel;
using Microsoft.SemanticKernel;
using TILSOFTAI.Orchestration.SK;

namespace TILSOFTAI.Orchestration.SK.Plugins;

public sealed class OrdersToolsPlugin
{
    private readonly ToolInvoker _invoker;

    public OrdersToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("query")]
    [Description("Tra cứu danh sách đơn hàng (phân trang). Dùng khi user hỏi xem đơn hàng.")]
    public Task<object> QueryAsync(
        Guid? customerId = null,
        string? status = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("orders.query", new { customerId, status, startDate, endDate, page, pageSize }, ct);

    [KernelFunction("summary")]
    [Description("Tổng hợp KPI đơn hàng theo bộ lọc. Dùng khi user hỏi phân tích/tổng hợp đơn hàng.")]
    public Task<object> SummaryAsync(
        Guid? customerId = null,
        string? status = null,
        DateTimeOffset? startDate = null,
        DateTimeOffset? endDate = null,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("orders.summary", new { customerId, status, startDate, endDate }, ct);

    [KernelFunction("create_prepare")]
    [Description("Chuẩn bị tạo đơn hàng. KHÔNG commit. Trả confirmationId + preview.")]
    public Task<object> CreatePrepareAsync(
        Guid customerId,
        Guid modelId,
        string? color = null,
        int quantity = 1,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("orders.create.prepare", new { customerId, modelId, color, quantity }, ct);

    [KernelFunction("create_commit")]
    [Description("Commit tạo đơn hàng sau khi user xác nhận. Yêu cầu confirmationId.")]
    public Task<object> CreateCommitAsync(
        string confirmationId,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("orders.create.commit", new { confirmationId }, ct);
}
