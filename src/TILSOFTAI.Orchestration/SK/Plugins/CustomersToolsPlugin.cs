using System.ComponentModel;
using Microsoft.SemanticKernel;
using TILSOFTAI.Orchestration.SK;

namespace TILSOFTAI.Orchestration.SK.Plugins;

public sealed class CustomersToolsPlugin
{
    private readonly ToolInvoker _invoker;

    public CustomersToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("search")]
    [Description("Tìm khách hàng theo tên/email/mã để lấy customerId.")]
    public Task<object> SearchAsync(
        string query,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("customers.search", new { query, page, pageSize }, ct);

    [KernelFunction("update_email")]
    [Description("Chuẩn bị cập nhật email khách hàng. Trả confirmationId. KHÔNG commit.")]
    public Task<object> UpdateEmailAsync(
        Guid customerId,
        string newEmail,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("customers.updateEmail", new { customerId, newEmail }, ct);
}