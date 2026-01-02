using System.ComponentModel;
using Microsoft.SemanticKernel;
using TILSOFTAI.Orchestration.SK;

namespace TILSOFTAI.Orchestration.SK.Plugins;

[SkModule("customers")]
public sealed class CustomersToolsPlugin
{
    private readonly ToolInvoker _invoker;

    public CustomersToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("search")]
    [Description("Tìm khách hàng theo bộ lọc động. filters có thể gồm: query (tên/email/mã).")]
    public Task<object> SearchAsync(
        Dictionary<string, string?>? filters = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("customers.search", new { filters, page, pageSize }, ct);

    [KernelFunction("update_email")]
    [Description("Cập nhật email khách hàng. Nếu chưa có confirmationId thì chỉ chuẩn bị, KHÔNG commit.")]
    public Task<object> UpdateEmailAsync(
        Guid? customerId = null,
        string? email = null,
        string? confirmationId = null,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("customers.updateEmail", new { customerId, email, confirmationId }, ct);
}
