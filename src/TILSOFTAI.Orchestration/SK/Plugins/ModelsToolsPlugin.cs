using System.ComponentModel;
using Microsoft.SemanticKernel;
using System.Text.Json;
using TILSOFTAI.Orchestration.SK;

namespace TILSOFTAI.Orchestration.SK.Plugins;

// NOTE: ModuleRouter routes to module key "models".
// Keep module metadata consistent to ensure tools are exposed.
[SkModule("models")]
public sealed class ModelsToolsPlugin
{
    private readonly ToolInvoker _invoker;

    public ModelsToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("count")]
    [Description("Đếm tổng số model theo bộ lọc động (season/collection/rangeName/modelCode/modelName).")]
    public Task<object> CountAsync(
        Dictionary<string, string?>? filters = null,
        CancellationToken ct = default)
    => _invoker.ExecuteAsync("models.count", new { filters }, ct);

    [KernelFunction("search")]
    [Description("Tìm model theo bộ lọc động. filters có thể gồm: season, collection, rangeName, modelCode, modelName.")]
    public Task<object> SearchAsync(
        Dictionary<string, string?>? filters = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    => _invoker.ExecuteAsync("models.search", new { filters, page, pageSize }, ct);

    [KernelFunction("stats")]
    [Description("Thống kê model theo nhiều chiều (rangeName/collection/season). Trả contract models.stats.v1.")]
    public Task<object> StatsAsync(
        Dictionary<string, string?>? filters = null,
        int topN = 10,
        CancellationToken ct = default)
    => _invoker.ExecuteAsync("models.stats", new { filters, topN }, ct);

    [KernelFunction("options")]
    [Description("Lấy đầy đủ nhóm tuỳ chọn và giá trị cho 1 model. Trả contract models.options.v1. modelId lấy từ models.search (ModelID).")]
    public Task<object> OptionsAsync(
        int modelId,
        bool includeConstraints = true,
        CancellationToken ct = default)
    => _invoker.ExecuteAsync("models.options", new { modelId, includeConstraints }, ct);

    [KernelFunction("get")]
    [Description("Lấy chi tiết model theo modelId.")]
    public Task<object> GetAsync(Guid modelId, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.get", new { modelId }, ct);

    [KernelFunction("attributes_list")]
    [Description("Danh sách attributes của model.")]
    public Task<object> AttributesListAsync(Guid modelId, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.attributes.list", new { modelId }, ct);

    [KernelFunction("price_analyze")]
    [Description("Phân tích giá model theo season/metric.")]
    public Task<object> PriceAnalyzeAsync(Guid modelId, string season, string metric, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.price.analyze", new { modelId, season, metric }, ct);

    [KernelFunction("create_prepare")]
    [Description("Chuẩn bị tạo model. KHÔNG commit. Trả confirmationId + preview.")]
    public Task<object> CreatePrepareAsync(
        string name,
        string category,
        decimal basePrice,
        IReadOnlyDictionary<string, string>? attributes = null,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.create.prepare", new { name, category, basePrice, attributes = attributes ?? new Dictionary<string, string>() }, ct);

    [KernelFunction("create_commit")]
    [Description("Commit tạo model sau khi user xác nhận. Yêu cầu confirmationId.")]
    public Task<object> CreateCommitAsync(string confirmationId, CancellationToken ct = default)
        => _invoker.ExecuteAsync("models.create.commit", new { confirmationId }, ct);

}
