using System.Text;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Orchestration.Formatting;

public sealed class MarkdownTableRenderOptions
{
    public int MaxRows { get; set; } = 20;
    public int MaxColumns { get; set; } = 12;
    public int MaxCellChars { get; set; } = 80;
}

public sealed class MarkdownTableRenderer
{
    public static string Render(
        IReadOnlyList<string>? columns,
        IEnumerable<object?[]>? rows,
        MarkdownTableRenderOptions? options = null)
    {
        options ??= new MarkdownTableRenderOptions();

        var maxRows = Math.Clamp(options.MaxRows, 1, 200);
        var maxCols = Math.Clamp(options.MaxColumns, 1, 50);
        var maxCellChars = Math.Clamp(options.MaxCellChars, 4, 500);

        var rowList = new List<object?[]>();
        var totalRows = 0;

        if (rows is not null)
        {
            if (rows is IReadOnlyCollection<object?[]> known)
            {
                totalRows = known.Count;
                foreach (var row in known.Take(maxRows))
                {
                    rowList.Add(row ?? Array.Empty<object?>());
                }
            }
            else
            {
                foreach (var row in rows)
                {
                    if (totalRows < maxRows)
                        rowList.Add(row ?? Array.Empty<object?>());
                    totalRows++;
                }
            }
        }

        var colList = BuildColumns(columns, rowList, maxCols);

        var sb = new StringBuilder();
        AppendRow(sb, colList.Select(c => FormatCell(c, maxCellChars)));
        AppendSeparator(sb, colList.Count);

        foreach (var row in rowList)
        {
            var cells = new string[colList.Count];
            for (var i = 0; i < colList.Count; i++)
            {
                var value = i < row.Length ? row[i] : null;
                cells[i] = FormatCell(value, maxCellChars);
            }
            AppendRow(sb, cells);
        }

        if (totalRows > rowList.Count)
        {
            sb.AppendLine($"_Đã hiển thị {rowList.Count}/{totalRows} dòng._");
        }

        return sb.ToString().TrimEnd();
    }

    public static string Render(TabularData table, MarkdownTableRenderOptions? options = null)
    {
        if (table is null)
            return Render(Array.Empty<string>(), Array.Empty<object?[]>(), options);

        var cols = table.Columns.Select(c => c.Name).ToList();
        return Render(cols, table.Rows, options);
    }

    public static string Render(AnalyticsSchema schema, IEnumerable<object?[]> rows, MarkdownTableRenderOptions? options = null)
    {
        if (schema is null)
            return Render(Array.Empty<string>(), rows, options);

        var cols = schema.Columns
            .Select(c => string.IsNullOrWhiteSpace(c.DisplayName) ? c.Name : c.DisplayName)
            .ToList();

        return Render(cols, rows, options);
    }

    private static List<string> BuildColumns(IReadOnlyList<string>? columns, List<object?[]> rowList, int maxCols)
    {
        var list = new List<string>(maxCols);
        if (columns is not null && columns.Count > 0)
        {
            foreach (var c in columns)
            {
                if (list.Count >= maxCols)
                    break;
                list.Add(string.IsNullOrWhiteSpace(c) ? $"col{list.Count + 1}" : c.Trim());
            }
        }

        if (list.Count == 0)
        {
            var colCount = rowList.Count > 0 ? Math.Max(1, rowList[0].Length) : 1;
            for (var i = 0; i < Math.Min(colCount, maxCols); i++)
                list.Add($"col{i + 1}");
        }

        return list;
    }

    private static void AppendRow(StringBuilder sb, IEnumerable<string> cells)
    {
        sb.Append("| ");
        sb.Append(string.Join(" | ", cells));
        sb.AppendLine(" |");
    }

    private static void AppendSeparator(StringBuilder sb, int colCount)
    {
        sb.Append("| ");
        for (var i = 0; i < colCount; i++)
        {
            if (i > 0) sb.Append(" | ");
            sb.Append("---");
        }
        sb.AppendLine(" |");
    }

    private static string FormatCell(object? value, int maxChars)
    {
        if (value is null)
            return string.Empty;

        var text = value.ToString() ?? string.Empty;
        text = text.Replace("\r", string.Empty)
                   .Replace("\n", "\\n")
                   .Replace("|", "\\|");

        if (text.Length > maxChars)
        {
            if (maxChars <= 3)
                return text.Substring(0, maxChars);
            return text.Substring(0, maxChars - 3) + "...";
        }

        return text;
    }
}

public sealed record AnalyticsSchema(IReadOnlyList<AnalyticsService.AnalyticsColumn> Columns);
