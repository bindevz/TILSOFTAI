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
        return c.Type switch
        {
            TabularType.Int32 => new Int32DataFrameColumn(c.Name, rowCount),
            TabularType.Int64 => new Int64DataFrameColumn(c.Name, rowCount),
            TabularType.Double => new DoubleDataFrameColumn(c.Name, rowCount),
            TabularType.Decimal => new DecimalDataFrameColumn(c.Name, rowCount),
            TabularType.Boolean => new BooleanDataFrameColumn(c.Name, rowCount),
            TabularType.DateTime => new DateTimeDataFrameColumn(c.Name, rowCount),
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
                TabularType.Int64 => Convert.ToInt64(value),
                TabularType.Double => Convert.ToDouble(value),
                TabularType.Decimal => Convert.ToDecimal(value),
                TabularType.Boolean => Convert.ToBoolean(value),
                TabularType.DateTime => CoerceDateTime(value),
                _ => value.ToString()
            };
        }
        catch
        {
            return type == TabularType.String ? value.ToString() : null;
        }
    }

    private static DateTime? CoerceDateTime(object value)
    {
        if (value is DateTimeOffset dto)
            return dto.UtcDateTime;

        if (value is DateTime dt)
        {
            return dt.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
                : dt.ToUniversalTime();
        }

        if (value is string s && DateTime.TryParse(s, out var parsed))
        {
            return parsed.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : parsed.ToUniversalTime();
        }

        var converted = Convert.ToDateTime(value);
        return converted.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(converted, DateTimeKind.Utc)
            : converted.ToUniversalTime();
    }
}
