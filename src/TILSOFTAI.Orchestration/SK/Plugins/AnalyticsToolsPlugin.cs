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
    [Description("Chạy pipeline phân tích (groupBy/sort/topN/select/filter) trên datasetId.")]
    public Task<object> RunAsync(
        string datasetId,
        [Description("Pipeline JSON array. Mỗi step là object với 'op' (filter/groupBy/sort/topN/select).")]
        JsonElement pipeline,
        int topN = 20,
        int maxGroups = 200,
        int maxResultRows = 500,
        CancellationToken ct = default)
        => _invoker.ExecuteAsync("analytics.run", new { datasetId, pipeline, topN, maxGroups, maxResultRows }, ct);

    [KernelFunction("atomic_query_execute")]
    [Description("Thực thi stored procedure theo chuẩn AtomicQuery (RS0 schema, RS1 summary, RS2..N data tables). Tự routing theo RS0.delivery/tableKind: trả displayTables và/hoặc engineDatasets (datasetId) cho Atomic Data Engine.")]
    public Task<object> AtomicQueryExecuteAsync(
        [Description("Tên stored procedure. Bắt buộc: dbo.TILSOFTAI_sp_*")]
        string spName,
        [Description("Tham số SP dạng JSON object. Key là tên tham số (có thể bỏ '@').")]
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
