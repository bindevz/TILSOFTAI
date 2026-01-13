using System.Data.Common;
using System.Linq;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Infrastructure.Data.Tabular;

/// <summary>
/// Reader for standardized stored procedure output:
/// - RS0: schema (resultset + column rows)
/// - RS1: summary (optional)
/// - RS2..RSN: data tables
///
/// RS0 format follows "TILSOFTAI_sp_AtomicQuery_Template".
/// </summary>
public static class SqlAtomicQueryReader
{
    public sealed record ReadOptions(
        int MaxRowsPerTable = 20_000,
        int MaxRowsSummary = 500,
        int MaxSchemaRows = 50_000);

    public static async Task<AtomicQueryResult> ReadAsync(
        DbDataReader reader,
        ReadOptions? options,
        CancellationToken cancellationToken)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));
        options ??= new ReadOptions();

        // -----------------------------
        // RS0: Schema
        // -----------------------------
        var schemaRows = await ReadSchemaRowsAsync(reader, options.MaxSchemaRows, cancellationToken);
        var schema = BuildSchema(schemaRows);

        // Move to RS1 (if any)
        var tables = new List<AtomicResultSet>();
        AtomicResultSet? summary = null;

        
        // Schema declares indices starting from 1 (RS1=1). We'll read in ascending order when schema is present.
        var ordered = schema.ResultSets.OrderBy(x => x.Index).ToList();

        if (ordered.Count > 0)
        {
            foreach (var rs in ordered)
            {
                if (!await reader.NextResultAsync(cancellationToken))
                    break;

                var maxRows = rs.TableKind?.Equals("summary", StringComparison.OrdinalIgnoreCase) == true
                    ? options.MaxRowsSummary
                    : options.MaxRowsPerTable;

                var table = await DbDataReaderTabularMaterializer.ReadAsync(reader, maxRows, cancellationToken);

                // Bind unknown columns by name.
                var boundSchema = BindUnknownColumns(rs, table.Columns);

                var item = new AtomicResultSet(boundSchema, table);

                if (rs.TableKind?.Equals("summary", StringComparison.OrdinalIgnoreCase) == true ||
                    rs.TableName.Equals("summary", StringComparison.OrdinalIgnoreCase))
                    summary = item;
                else
                    tables.Add(item);
            }

            return new AtomicQueryResult(schema, summary, tables);
        }

        // Fallback: RS0 schema is missing or unreadable.
        // In this case, we still need to return something useful to the caller (and avoid "empty evidence" loops).
        // Strategy: read ALL remaining result sets sequentially, infer a minimal schema per result set, and mark
        // a small 1-row table containing common count/hint columns as "summary".
        var fallbackResultSets = new List<AtomicResultSetSchema>();
        var fallbackIndex = 1;

        while (await reader.NextResultAsync(cancellationToken))
        {
            var table = await DbDataReaderTabularMaterializer.ReadAsync(reader, options.MaxRowsPerTable, cancellationToken);

            var kindGuess = GuessTableKind(table);
            var rs = new AtomicResultSetSchema(
                Index: fallbackIndex,
                TableName: $"rs{fallbackIndex}",
                TableKind: kindGuess,
                Delivery: "auto");

            fallbackResultSets.Add(rs);

            var boundSchema = BindUnknownColumns(rs, table.Columns);
            var item = new AtomicResultSet(boundSchema, table);

            if (kindGuess == "summary" && summary is null)
                summary = item;
            else
                tables.Add(item);

            fallbackIndex++;
        }

        var fallbackSchema = new AtomicQuerySchema(fallbackResultSets);
        return new AtomicQueryResult(fallbackSchema, summary, tables);

    }

    /// <summary>
    /// Best-effort classification used only when RS0 schema is missing/unreadable.
    /// We keep this heuristic conservative to avoid mislabeling datasets.
    /// </summary>
    private static string? GuessTableKind(TabularData table)
    {
        if (table.Rows is null || table.Rows.Count == 0) return null;

        // Heuristic: summary tables are usually very small and contain common control/count fields.
        if (table.Rows.Count <= 5)
        {
            var names = table.Columns
                .Select(c => c.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Common summary signals in TILSOFT atomic SPs.
            var summarySignals = new[]
            {
                "totalCount", "count", "rowCount", "total", "isDatasetMode",
                "page", "size", "engineRows", "displayRows",
                "seasonFilter", "collectionFilter", "rangeNameFilter"
            };

            if (summarySignals.Any(s => names.Contains(s)))
                return "summary";

            // Fallback: if it has only a few scalar columns, treat as summary.
            if (table.Columns.Count <= 12)
                return "summary";
        }

        return null;
    }

    // -----------------------------
    // Schema parsing
    // -----------------------------

    private sealed record SchemaRow(
        string? RecordType,
        int? ResultSetIndex,
        string? TableName,
        string? TableKind,
        string? Delivery,
        string? Grain,
        string? PrimaryKey,
        string? JoinHints,
        string? DescVi,
        string? DescEn,
        string? ColumnName,
        int? Ordinal,
        string? SqlType,
        string? TabularType,
        string? Role,
        string? SemanticType,
        string? Unit,
        string? Format,
        bool? Nullable,
        string? Example,
        string? Notes,
        string? Vi,
        string? En);

    private static async Task<List<SchemaRow>> ReadSchemaRowsAsync(DbDataReader reader, int maxRows, CancellationToken ct)
    {
        // Heuristic: must have at least columnName or recordType+resultSetIndex.
        var ordRecordType = FindOrdinal(reader, "recordType", "type");
        var ordRs = FindOrdinal(reader, "resultSetIndex", "resultsetindex", "rsIndex", "rs", "resultSet");
        var ordTableName = FindOrdinal(reader, "tableName", "name", "dataset", "table");
        var ordColName = FindOrdinal(reader, "columnName", "column", "col", "field", "fieldName");
        if (ordRs < 0 && ordColName < 0)
            return new List<SchemaRow>(0);

        var ordTableKind = FindOrdinal(reader, "tableKind", "kind");
        var ordDelivery = FindOrdinal(reader, "delivery", "target", "audience");
        var ordGrain = FindOrdinal(reader, "grain");
        var ordPk = FindOrdinal(reader, "primaryKey", "pk");
        var ordJoin = FindOrdinal(reader, "joinHints", "joins", "join");
        var ordDescVi = FindOrdinal(reader, "description_vi", "desc_vi", "vi_description", "descriptionVi", "descVi");
        var ordDescEn = FindOrdinal(reader, "description_en", "desc_en", "en_description", "descriptionEn", "descEn");
        var ordOrdinal = FindOrdinal(reader, "ordinal", "ord");
        var ordSqlType = FindOrdinal(reader, "sqlType", "sql_type");
        var ordTabType = FindOrdinal(reader, "tabularType", "tab_type", "tabType");
        var ordRole = FindOrdinal(reader, "role");
        var ordSemType = FindOrdinal(reader, "semanticType", "semantic_type", "semType");
        var ordUnit = FindOrdinal(reader, "unit", "uom");
        var ordFormat = FindOrdinal(reader, "format", "fmt");
        var ordNullable = FindOrdinal(reader, "nullable", "isNullable");
        var ordExample = FindOrdinal(reader, "example", "sample");
        var ordNotes = FindOrdinal(reader, "notes", "note", "comment");
        var ordVi = FindOrdinal(reader, "vi", "label_vi", "title_vi");
        var ordEn = FindOrdinal(reader, "en", "label_en", "title_en");

        var rows = new List<SchemaRow>(capacity: 256);
        while (rows.Count < maxRows && await reader.ReadAsync(ct))
        {
            string? GetString(int ord) => ord < 0 ? null : reader.IsDBNull(ord) ? null : (reader.GetValue(ord)?.ToString());
            int? GetInt(int ord)
            {
                if (ord < 0 || reader.IsDBNull(ord)) return null;
                var v = reader.GetValue(ord);
                if (v is int i) return i;
                if (int.TryParse(v?.ToString(), out var n)) return n;
                return null;
            }
            bool? GetBool(int ord)
            {
                if (ord < 0 || reader.IsDBNull(ord)) return null;
                var v = reader.GetValue(ord);
                if (v is bool b) return b;
                if (v is int i) return i != 0;
                if (bool.TryParse(v?.ToString(), out var bb)) return bb;
                return null;
            }

            rows.Add(new SchemaRow(
                RecordType: GetString(ordRecordType),
                ResultSetIndex: GetInt(ordRs),
                TableName: GetString(ordTableName),
                TableKind: GetString(ordTableKind),
                Delivery: GetString(ordDelivery),
                Grain: GetString(ordGrain),
                PrimaryKey: GetString(ordPk),
                JoinHints: GetString(ordJoin),
                DescVi: GetString(ordDescVi),
                DescEn: GetString(ordDescEn),
                ColumnName: GetString(ordColName),
                Ordinal: GetInt(ordOrdinal),
                SqlType: GetString(ordSqlType),
                TabularType: GetString(ordTabType),
                Role: GetString(ordRole),
                SemanticType: GetString(ordSemType),
                Unit: GetString(ordUnit),
                Format: GetString(ordFormat),
                Nullable: GetBool(ordNullable),
                Example: GetString(ordExample),
                Notes: GetString(ordNotes),
                Vi: GetString(ordVi),
                En: GetString(ordEn)
            ));
        }

        return rows;
    }

    private static AtomicQuerySchema BuildSchema(List<SchemaRow> rows)
    {
        var rsMeta = new Dictionary<int, AtomicResultSetSchema>();

        var colsByRs = new Dictionary<int, List<AtomicColumnSchema>>();

        foreach (var r in rows)
        {
            var rs = r.ResultSetIndex ?? 0;
            if (rs <= 0)
                continue;

            var recordType = (r.RecordType ?? string.Empty).Trim().ToLowerInvariant();

            // Treat missing recordType as "column" if columnName is present.
            if (string.IsNullOrEmpty(recordType) && !string.IsNullOrWhiteSpace(r.ColumnName))
                recordType = "column";

            if (recordType == "resultset")
            {
                rsMeta[rs] = new AtomicResultSetSchema(
                    Index: rs,
                    TableName: string.IsNullOrWhiteSpace(r.TableName) ? $"rs{rs}" : r.TableName!,
                    TableKind: r.TableKind,
                    Delivery: r.Delivery,
                    Grain: r.Grain,
                    PrimaryKey: SplitCsv(r.PrimaryKey),
                    JoinHints: r.JoinHints,
                    DescriptionVi: r.DescVi,
                    DescriptionEn: r.DescEn,
                    Columns: null,
                    UnknownColumns: null
                );
                continue;
            }

            if (recordType == "column")
            {
                if (string.IsNullOrWhiteSpace(r.ColumnName))
                    continue;

                if (!colsByRs.TryGetValue(rs, out var list))
                {
                    list = new List<AtomicColumnSchema>();
                    colsByRs[rs] = list;
                }

                list.Add(new AtomicColumnSchema(
                    Name: r.ColumnName!,
                    Ordinal: r.Ordinal,
                    SqlType: r.SqlType,
                    TabularType: r.TabularType,
                    Role: r.Role,
                    SemanticType: r.SemanticType,
                    Unit: r.Unit,
                    Format: r.Format,
                    Nullable: r.Nullable,
                    Example: r.Example,
                    Notes: r.Notes,
                    Vi: r.Vi,
                    En: r.En
                ));
            }
        }

        // Ensure every RS that has columns has a resultset meta row.
        foreach (var rs in colsByRs.Keys)
        {
            if (!rsMeta.ContainsKey(rs))
            {
                rsMeta[rs] = new AtomicResultSetSchema(
                    Index: rs,
                    TableName: $"rs{rs}",
                    TableKind: null,
                    Delivery: null);
            }
        }

        

        // Fill gaps to prevent misalignment between declared indices and reader.NextResult().
        if (rsMeta.Count > 0)
        {
            var max = rsMeta.Keys.Max();
            for (var i = 1; i <= max; i++)
            {
                if (!rsMeta.ContainsKey(i))
                {
                    rsMeta[i] = new AtomicResultSetSchema(
                        Index: i,
                        TableName: $"rs{i}",
                        TableKind: null,
                        Delivery: null);
                }
            }
        }
// Attach columns into schema (sorted by ordinal if available)
        var resultSets = new List<AtomicResultSetSchema>();
        foreach (var kv in rsMeta.OrderBy(k => k.Key))
        {
            colsByRs.TryGetValue(kv.Key, out var cols);
            var orderedCols = (cols ?? new List<AtomicColumnSchema>())
                .OrderBy(c => c.Ordinal ?? int.MaxValue)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            resultSets.Add(kv.Value with { Columns = orderedCols });
        }

        return new AtomicQuerySchema(resultSets);
    }

    private static AtomicResultSetSchema BindUnknownColumns(AtomicResultSetSchema schema, IReadOnlyList<TabularColumn> actualColumns)
    {
        var metaCols = schema.Columns?.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unknown = new List<string>();

        foreach (var c in actualColumns)
        {
            if (!metaCols.Contains(c.Name))
                unknown.Add(c.Name);
        }

        // also include meta columns that are missing in actual columns
        var actualSet = actualColumns.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (schema.Columns is not null)
        {
            foreach (var mc in schema.Columns)
            {
                if (!actualSet.Contains(mc.Name))
                    unknown.Add($"(missing){mc.Name}");
            }
        }

        return schema with { UnknownColumns = unknown };
    }

    private static IReadOnlyList<string>? SplitCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return null;
        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private static int FindOrdinal(DbDataReader reader, params string[] candidates)
    {
        if (candidates is null || candidates.Length == 0) return -1;

        for (var i = 0; i < reader.FieldCount; i++)
        {
            var n = reader.GetName(i);
            foreach (var c in candidates)
            {
                if (n.Equals(c, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }
}
