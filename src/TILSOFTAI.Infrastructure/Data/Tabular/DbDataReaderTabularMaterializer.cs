using System.Data.Common;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Infrastructure.Data.Tabular;

/// <summary>
/// Auto-materializes TabularData from a DbDataReader without any pre-declared column list.
///
/// Notes:
/// - Column names are taken directly from the reader (SQL aliases are authoritative).
/// - Types are inferred from DbDataReader.GetFieldType(i) and mapped to TabularType.
/// - Rows are bounded by maxRows; DBNull is normalized to null.
/// </summary>
public static class DbDataReaderTabularMaterializer
{
    public static async Task<TabularData> ReadAsync(
        DbDataReader reader,
        int maxRows,
        CancellationToken cancellationToken)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        maxRows = Math.Clamp(maxRows, 0, 200_000);

        var fieldCount = reader.FieldCount;

        var columns = new List<TabularColumn>(fieldCount);
        for (var i = 0; i < fieldCount; i++)
        {
            var name = reader.GetName(i);
            var type = MapToTabularType(reader.GetFieldType(i));
            columns.Add(new TabularColumn(name, type));
        }

        var rows = new List<object?[]>(capacity: Math.Min(maxRows, 2048));

        if (maxRows == 0)
            return new TabularData(columns, rows, TotalCount: null);

        var buffer = new object[fieldCount];
        while (rows.Count < maxRows && await reader.ReadAsync(cancellationToken))
        {
            reader.GetValues(buffer);

            var row = new object?[fieldCount];
            for (var i = 0; i < fieldCount; i++)
                row[i] = buffer[i] == DBNull.Value ? null : buffer[i];

            rows.Add(row);
        }

        return new TabularData(columns, rows, TotalCount: null);
    }

    private static TabularType MapToTabularType(Type t)
    {
        // Prefer the most precise TabularType available. TabularType is intentionally small.
        if (t == typeof(string) || t == typeof(char) || t == typeof(Guid)) return TabularType.String;

        if (t == typeof(byte) || t == typeof(short) || t == typeof(int)) return TabularType.Int32;

        // Int64 not available in TabularType; represent as String to avoid overflow or lossy conversion.
        if (t == typeof(long)) return TabularType.String;

        if (t == typeof(bool)) return TabularType.Boolean;
        if (t == typeof(float) || t == typeof(double)) return TabularType.Double;
        if (t == typeof(decimal)) return TabularType.Decimal;
        if (t == typeof(DateTime) || t == typeof(DateTimeOffset)) return TabularType.DateTime;

        // Fallback: serialize as string on the client side if needed.
        return TabularType.String;
    }
}
