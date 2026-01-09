using Microsoft.Data.Analysis;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Application.Analytics;

/// <summary>
/// Converts TabularData (schema + object?[] rows) into a Microsoft.Data.Analysis DataFrame.
///
/// This keeps analytics flows free of domain entities/DTOs and allows multiple datasources to share
/// a single, predictable ingestion path.
/// </summary>
public static class TabularDataFrameBuilder
{
    public static DataFrame Build(TabularData tabular)
    {
        tabular ??= new TabularData(Array.Empty<TabularColumn>(), Array.Empty<object?[]>(), null);
        var rowCount = tabular.Rows?.Count ?? 0;

        var columns = new List<DataFrameColumn>(tabular.Columns.Count);
        foreach (var c in tabular.Columns)
        {
            columns.Add(CreateColumn(c, rowCount));
        }

        var df = new DataFrame(columns);

        // Populate values
        for (var r = 0; r < rowCount; r++)
        {
            var row = tabular.Rows[r];
            for (var i = 0; i < columns.Count; i++)
            {
                var colSpec = tabular.Columns[i];
                var v = (row is null || i >= row.Length) ? null : row[i];
                df.Columns[i][r] = CoerceValue(v, colSpec.Type);
            }
        }

        return df;
    }

    private static DataFrameColumn CreateColumn(TabularColumn c, int rowCount)
    {
        // Keep the implementation conservative: only reference column types we already rely on.
        // This avoids version-specific column classes breaking compilation.
        return c.Type switch
        {
            TabularType.Int32 => new Int32DataFrameColumn(c.Name, rowCount),
            TabularType.Double => new DoubleDataFrameColumn(c.Name, rowCount),
            TabularType.Decimal => new DoubleDataFrameColumn(c.Name, rowCount),
            _ => new StringDataFrameColumn(c.Name, rowCount)
        };
    }

    private static object? CoerceValue(object? value, TabularType type)
    {
        if (value is null) return null;

        try
        {
            return type switch
            {
                TabularType.Int32 => Convert.ToInt32(value),
                TabularType.Double => Convert.ToDouble(value),
                TabularType.Decimal => Convert.ToDouble(value),
                TabularType.Boolean => value.ToString(),
                TabularType.DateTime => value.ToString(),
                _ => value.ToString()
            };
        }
        catch
        {
            // Fail soft: keep the pipeline running and let analysis steps treat it as string.
            return value.ToString();
        }
    }
}
