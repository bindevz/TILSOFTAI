using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace TILSOFTAI.Orchestration.SK.Plugins;

[SkModule("models")]
public sealed class ModelsWriteToolsPlugin
{
    private readonly ToolInvoker _invoker;
    public ModelsWriteToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("create_prepare")]
    [Description("Chuẩn bị tạo model mới. Trả confirmation_id để user xác nhận.")]
    public Task<object> CreatePrepareAsync(string name, string category, decimal basePrice, Dictionary<string, string>? attributes = null, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.create.prepare", new { name, category, basePrice, attributes = attributes ?? new Dictionary<string, string>() }, ct);

    [KernelFunction("create_commit")]
    [Description("Commit tạo model sau khi user xác nhận bằng confirmationId.")]
    public Task<object> CreateCommitAsync(string confirmationId, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.create.commit", new { confirmationId }, ct);
}
