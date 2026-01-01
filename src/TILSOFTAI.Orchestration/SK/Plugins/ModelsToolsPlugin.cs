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
    [Description("Đếm tổng số model. Có thể lọc theo season (vd: '24/25' hoặc 2025/2025).")]
    public async Task<object> CountAsync(
        string? rangeName = null,
        string? modelCode = null,
        string? modelName = null,
        string? season = null,
        string? collection = null,
        CancellationToken ct = default)
    {
        var raw = await _invoker.ExecuteAsync(
            "models.search",
            new { rangeName, modelCode, modelName, season, collection, page = 1, pageSize = 1 },
            ct);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(raw, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            (data.TryGetProperty("totalCount", out var total) || data.TryGetProperty("TotalCount", out total)) &&
            (total.ValueKind == JsonValueKind.Number || total.ValueKind == JsonValueKind.String))
        {
            var n = total.ValueKind == JsonValueKind.Number
                ? total.GetInt32()
                : int.TryParse(total.GetString(), out var parsed) ? parsed : 0;

            return new { totalCount = n, season };
        }

        return raw;
    }


    [KernelFunction("search")]
    [Description("Tìm model theo các bộ lọc. Dùng season dạng '24/25' hoặc '2024/2025'. Kết quả có TotalCount.")]
    public Task<object> SearchAsync(
        string? rangeName = null,
        string? modelCode = null,
        string? modelName = null,
        string? season = null,
        string? collection = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    => _invoker.ExecuteAsync("models.search", new { rangeName, modelCode, modelName, season, collection, page, pageSize }, ct);

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
