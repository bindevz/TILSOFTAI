using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace TILSOFTAI.Orchestration.SK.Plugins;

[SkModule("models")]
public sealed class ModelsOptionsToolsPlugin
{
    private readonly ToolInvoker _invoker;
    public ModelsOptionsToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("options")]
    [Description("Lấy option groups và constraints cho modelId (int, từ models.search ModelID).")]
    public Task<object> OptionsAsync(int modelId, bool includeConstraints = true, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.options", new { modelId, includeConstraints }, ct);

    [KernelFunction("attributes_list")]
    [Description("Liệt kê thuộc tính của model theo modelId (GUID).")]
    public Task<object> ListAttributesAsync(Guid modelId, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.attributes.list", new { modelId }, ct);
}
