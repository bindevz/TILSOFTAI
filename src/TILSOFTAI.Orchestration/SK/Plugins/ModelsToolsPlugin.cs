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

    [KernelFunction("filters_catalog")]
    [Description("Trả về danh sách filters hợp lệ khi gọi models.search/count/stats (keys, aliases, examples). Dùng khi cần biết có thể filter theo gì.")]
    public Task<object> FiltersCatalogAsync(CancellationToken ct = default)
    => _invoker.ExecuteAsync("models.filters_catalog", new { }, ct);

}
