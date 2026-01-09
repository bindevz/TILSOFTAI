using Microsoft.Data.Analysis;
using System.Text.Json;
using TILSOFTAI.Application.Analytics;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Application.Analytics.Sources;

/// <summary>
/// Analytics datasource for 'models'.
///
/// Notes:
/// - Returns a DataFrame built from raw tabular rows (TabularData).
/// - Does not materialize domain entities or DTOs for analytics workloads.
/// </summary>
public sealed class ModelsAnalyticsDataSource : IAnalyticsDataSource
{
    public string SourceName => "models";

    private readonly ModelsService _modelsService;

    public ModelsAnalyticsDataSource(ModelsService modelsService)
    {
        _modelsService = modelsService;
    }

    public async Task<DataFrame> FetchAsync(
        IReadOnlyDictionary<string, string?> filters,
        JsonElement? select,
        int maxRows,
        int maxColumns,
        TSExecutionContext context,
        CancellationToken cancellationToken)
    {
        // Fetch bounded raw tabular rows (no entities/DTOs).
        var tabular = await FetchModelsTabularAsync(filters, maxRows, context, cancellationToken);

        // Build a raw DataFrame.
        var df = TabularDataFrameBuilder.Build(tabular);

        // Apply select (optional), with a hard maxColumns cap.
        return ApplySelect(df, select, maxColumns);
    }

    private async Task<TabularData> FetchModelsTabularAsync(
        IReadOnlyDictionary<string, string?> filters,
        int maxRows,
        TSExecutionContext context,
        CancellationToken cancellationToken)
    {
        filters ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        filters.TryGetValue("rangeName", out var rangeName);
        filters.TryGetValue("modelCode", out var modelCode);
        filters.TryGetValue("modelName", out var modelName);
        filters.TryGetValue("season", out var season);
        filters.TryGetValue("collection", out var collection);

        var page = 1;
        var pageSize = Math.Min(500, Math.Max(1, maxRows));

        List<object?[]> allRows = new(capacity: Math.Min(maxRows, 2048));
        int? total = null;
        IReadOnlyList<TabularColumn>? cols = null;

        while (allRows.Count < maxRows)
        {
            var remaining = maxRows - allRows.Count;
            var size = Math.Min(pageSize, remaining);

            var chunk = await _modelsService.SearchTabularAsync(
                context.TenantId,
                rangeName,
                modelCode,
                modelName,
                season,
                collection,
                page,
                size,
                context,
                cancellationToken);

            cols ??= chunk.Columns;
            total ??= chunk.TotalCount;

            if (chunk.Rows is null || chunk.Rows.Count == 0)
                break;

            allRows.AddRange(chunk.Rows);
            if (chunk.Rows.Count < size)
                break;

            page++;
            if (page > 10_000) break; // hard safety
        }

        return new TabularData(cols ?? Array.Empty<TabularColumn>(), allRows, total);
    }

    private static DataFrame ApplySelect(DataFrame df, JsonElement? select, int maxColumns)
    {
        if (select is null || select.Value.ValueKind == JsonValueKind.Null)
            return df;

        if (select.Value.ValueKind != JsonValueKind.Array)
            return df;

        var requested = new List<string>();
        foreach (var e in select.Value.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.String) continue;
            var name = e.GetString();
            if (string.IsNullOrWhiteSpace(name)) continue;
            requested.Add(name.Trim());
        }

        if (requested.Count == 0)
            return df;

        requested = requested.Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, Math.Clamp(maxColumns, 1, 100)))
            .ToList();

        var cols = new List<DataFrameColumn>();
        foreach (var name in requested)
        {
            for (var i = 0; i < df.Columns.Count; i++)
            {
                if (string.Equals(df.Columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    cols.Add(df.Columns[i]);
                    break;
                }
            }
        }

        return cols.Count == 0 ? df : new DataFrame(cols);
    }
}
