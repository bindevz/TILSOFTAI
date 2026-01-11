using Microsoft.Data.Analysis;
using System.Text.Json;
using TILSOFTAI.Analytics;
using TILSOFTAI.Application.Analytics;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Application.Services;

/// <summary>
/// AnalyticsService (ver23-refactor):
/// - Acts as an orchestration/facade for analytics tools.
/// - Does NOT hard-code domain entities. Domain-specific fetching is delegated to IAnalyticsDataSource.
/// - Analysis is executed by the pure in-memory engine (TILSOFTAI.Analytics.AtomicDataEngine).
///
/// This keeps the "python-pandas before LLM" pattern:
/// server-side crunching -> compact summary -> LLM response.
/// </summary>
public sealed class AnalyticsService
{
    private static readonly TimeSpan DefaultDatasetTtl = TimeSpan.FromMinutes(10);

    private readonly IReadOnlyDictionary<string, IAnalyticsDataSource> _sources;
    private readonly IAnalyticsDatasetStore _datasetStore;
    private readonly IAuditLogger _auditLogger;
    private readonly AtomicDataEngine _engine;

    public AnalyticsService(
        IEnumerable<IAnalyticsDataSource> sources,
        IAnalyticsDatasetStore datasetStore,
        IAuditLogger auditLogger)
    {
        _sources = (sources ?? throw new ArgumentNullException(nameof(sources)))
            .GroupBy(s => s.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key.Trim().ToLowerInvariant(), g => g.First(), StringComparer.OrdinalIgnoreCase);

        _datasetStore = datasetStore;
        _auditLogger = auditLogger;
        _engine = new AtomicDataEngine();
    }

    public async Task<DatasetCreateResult> CreateDatasetAsync(
        string source,
        IReadOnlyDictionary<string, string?> filters,
        JsonElement? select,
        DatasetBounds bounds,
        TSExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("source is required.");
        source = source.Trim().ToLowerInvariant();
        bounds = bounds.WithDefaults();

        if (!_sources.TryGetValue(source, out var ds))
            throw new InvalidOperationException($"Source '{source}' is not supported. Supported: {string.Join(", ", _sources.Keys.OrderBy(x => x))}.");

        // Fetch bounded raw dataset (server-side). The datasource is responsible for tenant scoping.
        var df = await ds.FetchAsync(filters, select, bounds.MaxRows, bounds.MaxColumns, context, cancellationToken);

        var datasetId = Guid.NewGuid().ToString("N");
        var dataset = new AnalyticsDataset(datasetId, source, context.TenantId, context.UserId, DateTimeOffset.UtcNow, df);
        await _datasetStore.StoreAsync(datasetId, dataset, DefaultDatasetTtl, cancellationToken);

        var schema = DescribeSchema(df);
        var preview = BuildPreview(df, bounds.PreviewRows);

        var result = new DatasetCreateResult(
            DatasetId: datasetId,
            Source: source,
            RowCount: (int)df.Rows.Count,
            ColumnCount: df.Columns.Count,
            Schema: schema,
            Preview: preview,
            ExpiresAtUtc: dataset.CreatedAtUtc.Add(DefaultDatasetTtl));

        await _auditLogger.LogToolExecutionAsync(
            context,
            "analytics.dataset.create",
            new { source, filters, bounds },
            new { datasetId = result.DatasetId, result.RowCount, result.ColumnCount },
            cancellationToken);

        return result;
    }


    /// <summary>
    /// Creates a short-lived analytics dataset from an already materialized TabularData.
    /// This is used by "AtomicQuery" stored procedures that return multiple raw tables (RS2..N).
    /// </summary>
    public async Task<DatasetCreateResult> CreateDatasetFromTabularAsync(
        string source,
        TabularData tabular,
        DatasetBounds bounds,
        TSExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("source is required.");
        if (tabular is null) throw new ArgumentNullException(nameof(tabular));

        source = source.Trim().ToLowerInvariant();
        bounds = bounds.WithDefaults();

        // Build DataFrame
        var df = TabularDataFrameBuilder.Build(tabular);

        var datasetId = Guid.NewGuid().ToString("N");
        var dataset = new AnalyticsDataset(
            DatasetId: datasetId,
            Source: source,
            TenantId: context.TenantId,
            UserId: context.UserId,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Data: df);

        await _datasetStore.StoreAsync(datasetId, dataset, DefaultDatasetTtl, cancellationToken);

        var schema = DescribeSchema(df);
        var previewRows = bounds.PreviewRows <= 0 ? 0 : Math.Min(bounds.PreviewRows, tabular.Rows.Count);
        var preview = previewRows == 0 ? Array.Empty<object?[]>() : tabular.Rows.Take(previewRows).ToArray();

        var result = new DatasetCreateResult(
            DatasetId: datasetId,
            Source: source,
            RowCount: tabular.Rows.Count,
            ColumnCount: tabular.Columns.Count,
            Schema: schema,
            Preview: preview,
            ExpiresAtUtc: dataset.CreatedAtUtc.Add(DefaultDatasetTtl));

        await _auditLogger.LogToolExecutionAsync(
            context,
            "atomic.query.execute",
            new { source, bounds },
            new { result.RowCount, result.ColumnCount },
            cancellationToken);

        return result;
    }

    public async Task<RunResult> RunAsync(
        string datasetId,
        JsonElement pipeline,
        RunBounds bounds,
        TSExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(datasetId)) throw new ArgumentException("datasetId is required.");
        bounds = bounds.WithDefaults();

        if (!_datasetStore.TryGet(datasetId, out var obj) || obj is not AnalyticsDataset dataset)
            throw new InvalidOperationException("Dataset not found or expired.");

        if (!string.Equals(dataset.TenantId, context.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(dataset.UserId, context.UserId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Dataset does not belong to the current context.");

        var result = Summarize(dataset.Data, pipeline, bounds, datasetId);

        await _auditLogger.LogToolExecutionAsync(
            context,
            "analytics.run",
            new { datasetId, bounds },
            new { result.RowCount, result.ColumnCount },
            cancellationToken);

        return result;
    }

    /// <summary>
    /// Pure summarization entrypoint: takes an in-memory dataset and returns a compact tabular summary.
    /// This method is domain-agnostic and can be reused by other modules that already have a DataFrame.
    /// </summary>
    public RunResult Summarize(DataFrame dataset, JsonElement pipeline, RunBounds bounds, string datasetIdForTrace = "")
    {
        if (dataset is null) throw new ArgumentNullException(nameof(dataset));
        bounds = bounds.WithDefaults();

        var engineBounds = new AtomicDataEngine.EngineBounds(bounds.TopN, bounds.MaxGroups);
        var engineResult = _engine.Execute(dataset, pipeline, engineBounds);

        var df = engineResult.Data;
        var schema = DescribeSchema(df);
        var rows = BuildRows(df, bounds.MaxResultRows);

        return new RunResult(
            DatasetId: datasetIdForTrace,
            RowCount: rows.Count,
            ColumnCount: schema.Count,
            Schema: schema,
            Rows: rows,
            Warnings: engineResult.Warnings);
    }

    // ------------------------
    // DataFrame -> Tool Payload helpers
    // ------------------------

    private static List<AnalyticsColumn> DescribeSchema(DataFrame df)
    {
        var cols = new List<AnalyticsColumn>(df.Columns.Count);
        foreach (var c in df.Columns)
        {
            cols.Add(new AnalyticsColumn(
                Name: c.Name,
                DataType: c.DataType.Name,
                DisplayName: c.Name));
        }
        return cols;
    }

    private static List<object?[]> BuildPreview(DataFrame df, int previewRows)
    {
        previewRows = Math.Clamp(previewRows, 0, 200);
        return BuildRows(df, previewRows);
    }

    private static List<object?[]> BuildRows(DataFrame df, int maxRows)
    {
        var rows = new List<object?[]>(capacity: Math.Min(maxRows, 1024));
        var take = (int)Math.Min(df.Rows.Count, maxRows);
        for (var i = 0; i < take; i++)
        {
            var row = new object?[df.Columns.Count];
            for (var j = 0; j < df.Columns.Count; j++)
            {
                row[j] = df.Columns[j][i];
            }
            rows.Add(row);
        }
        return rows;
    }

    // ------------------------
    // Models
    // ------------------------

    public sealed record DatasetBounds(int MaxRows, int MaxColumns, int PreviewRows)
    {
        public DatasetBounds WithDefaults()
        {
            var maxRows = MaxRows <= 0 ? 20_000 : Math.Clamp(MaxRows, 1, 100_000);
            var maxCols = MaxColumns <= 0 ? 40 : Math.Clamp(MaxColumns, 1, 100);
            var preview = PreviewRows <= 0 ? 100 : Math.Clamp(PreviewRows, 0, 200);
            return new DatasetBounds(maxRows, maxCols, preview);
        }
    }

    public sealed record RunBounds(int TopN, int MaxGroups, int MaxResultRows)
    {
        public RunBounds WithDefaults()
        {
            var topN = TopN <= 0 ? 20 : Math.Clamp(TopN, 1, 200);
            var maxGroups = MaxGroups <= 0 ? 200 : Math.Clamp(MaxGroups, 1, 5_000);
            var maxRows = MaxResultRows <= 0 ? 500 : Math.Clamp(MaxResultRows, 1, 5_000);
            return new RunBounds(topN, maxGroups, maxRows);
        }
    }

    public sealed record AnalyticsColumn(string Name, string DataType, string DisplayName);

    internal sealed record AnalyticsDataset(
        string DatasetId,
        string Source,
        string TenantId,
        string UserId,
        DateTimeOffset CreatedAtUtc,
        DataFrame Data);

    public sealed record DatasetCreateResult(
        string DatasetId,
        string Source,
        int RowCount,
        int ColumnCount,
        IReadOnlyList<AnalyticsColumn> Schema,
        IReadOnlyList<object?[]> Preview,
        DateTimeOffset ExpiresAtUtc);

    public sealed record RunResult(
        string DatasetId,
        int RowCount,
        int ColumnCount,
        IReadOnlyList<AnalyticsColumn> Schema,
        IReadOnlyList<object?[]> Rows,
        IReadOnlyList<string> Warnings);
}
