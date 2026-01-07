using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace TILSOFTAI.Orchestration.SK.Plugins;

[SkModule("models")]
public sealed class ModelsPriceToolsPlugin
{
    private readonly ToolInvoker _invoker;
    public ModelsPriceToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("price_analyze")]
    [Description("Phân tích giá (costing breakdown) của model theo modelId (GUID).")]
    public Task<object> AnalyzePriceAsync(Guid modelId, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.price.analyze", new { modelId }, ct);
}
