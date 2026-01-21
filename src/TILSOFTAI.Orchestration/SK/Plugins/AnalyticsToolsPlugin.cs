using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Text.Json;

namespace TILSOFTAI.Orchestration.SK.Plugins;

[SkModule("analytics")]
public sealed class AnalyticsToolsPlugin
{
    private readonly ToolInvoker _invoker;

    public AnalyticsToolsPlugin(ToolInvoker invoker) => _invoker = invoker;

    [KernelFunction("run")]
    [Description("Chạy pipeline phân tích (groupBy/sort/topN/select/filter/derive/percentOfTotal/dateBucket) trên datasetId.")]
    public Task<object> RunAsync(
        string datasetId,
        [Description("Pipeline JSON array. Mỗi step là object với 'op' (filter/groupBy/sort/topN/select/derive/percentOfTotal/dateBucket).")]
        JsonElement pipeline,
        int topN = 20,
        int maxGroups = 200,
        int maxResultRows = 500,
        bool persistResult = false,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("analytics.run", new { datasetId, pipeline, topN, maxGroups, maxResultRows, persistResult }, ct);

    [KernelFunction("atomic_catalog_search")]
    [Description("Tìm stored procedure phù hợp trong catalog (full-text). RULE: Nếu không chắc spName, hãy gọi tool này trước rồi mới gọi atomic_query_execute.")]
    public Task<object> AtomicCatalogSearchAsync(
        [Description("Mô tả mục đích người dùng (có thể tiếng Việt/Anh).")]
        string query,
        int topK = 5,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("atomic.catalog.search", new { query, topK }, ct);

    [KernelFunction("atomic_query_execute")]
    [Description("Thực thi stored procedure theo chuẩn AtomicQuery (RS0 schema, RS1 summary, RS2..N data tables). Tự routing theo RS0.delivery/tableKind: trả displayTables và/hoặc engineDatasets (datasetId) cho Atomic Data Engine. Nếu không chắc spName, hãy gọi atomic_catalog_search trước.")]
    public Task<object> AtomicQueryExecuteAsync(
        [Description("Tên stored procedure. Bắt buộc: có trong catalog và theo chuẩn dbo.TILSOFTAI_sp_*")]
        string spName,
        [Description("Tham số SP dạng JSON object. Key là tên tham số (có thể bỏ '@'). BẮT BUỘC: chỉ dùng các key có trong ParamsJson (atomic.catalog.search -> results[].parameters[].name). Nếu có key lạ, tool sẽ fail-fast để tránh loop/nhầm ngữ cảnh.")]
        JsonElement? @params = null,
        int maxRowsPerTable = 20000,
        int maxRowsSummary = 500,
        int maxSchemaRows = 50000,
        int maxTables = 20,
        int maxColumns = 100,
        int maxDisplayRows = 2000,
        int previewRows = 100,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("atomic.query.execute", new
        {
            spName,
            @params,
            maxRowsPerTable,
            maxRowsSummary,
            maxSchemaRows,
            maxTables,
            maxColumns,
            maxDisplayRows,
            previewRows
        }, ct);
}

