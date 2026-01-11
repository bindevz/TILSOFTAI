using System.Data.Common;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Infrastructure.Data.Tabular;

/// <summary>
/// Reads ResultSet-0 metadata emitted by SQL and converts it into TabularSchema.
///
/// Expected columns (case-insensitive, aliases allowed):
/// - resultSetIndex (int) [optional if only one data result set]
/// - columnName (string)
/// - vi (string, optional)
/// - en (string, optional)
/// - role (string, optional)
/// - unit (string, optional)
/// - notes (string, optional)
///
/// This class is intentionally tolerant to allow SQL authors to use their own naming
/// conventions (e.g. ResultSet, Column, DescVI, DescEN...).
/// </summary>
public static class SqlResultSetMetadataReader
{
    private sealed record MetaRow(
        int? ResultSetIndex,
        string ColumnName,
        string? Vi,
        string? En,
        string? Role,
        string? Unit,
        string? Notes);

    public static async Task<(IReadOnlyList<TabularColumnSemantic> Semantics, bool IsMetadata)> TryReadMetadataAsync(
        DbDataReader reader,
        CancellationToken cancellationToken)
    {
        if (reader is null) throw new ArgumentNullException(nameof(reader));

        // Heuristic: metadata result set must have at least a column name field.
        var ordColName = FindOrdinal(reader, "columnName", "column", "col", "name");
        if (ordColName < 0)
            return (Array.Empty<TabularColumnSemantic>(), false);

        var ordRs = FindOrdinal(reader, "resultSetIndex", "resultSet", "rs", "resultSetNo", "resultSetNumber");
        var ordVi = FindOrdinal(reader, "vi", "descVi", "descriptionVi", "titleVi", "labelVi");
        var ordEn = FindOrdinal(reader, "en", "descEn", "descriptionEn", "titleEn", "labelEn");
        var ordRole = FindOrdinal(reader, "role", "semanticRole", "kind");
        var ordUnit = FindOrdinal(reader, "unit", "uom");
        var ordNotes = FindOrdinal(reader, "notes", "note", "comment");

        // If we only have a generic column called "name" but no other known metadata fields,
        // treat as non-metadata (reduces false positives).
        var hasAnySemanticField = ordVi >= 0 || ordEn >= 0 || ordRole >= 0 || ordUnit >= 0 || ordNotes >= 0 || ordRs >= 0;
        if (!hasAnySemanticField)
            return (Array.Empty<TabularColumnSemantic>(), false);

        var rows = new List<MetaRow>(capacity: 256);
        while (await reader.ReadAsync(cancellationToken))
        {
            var colName = GetString(reader, ordColName);
            if (string.IsNullOrWhiteSpace(colName))
                continue;

            int? rsIndex = null;
            if (ordRs >= 0)
            {
                var v = GetObject(reader, ordRs);
                if (v is not null)
                {
                    if (v is int i) rsIndex = i;
                    else if (int.TryParse(v.ToString(), out var parsed)) rsIndex = parsed;
                }
            }

            rows.Add(new MetaRow(
                ResultSetIndex: rsIndex,
                ColumnName: colName.Trim(),
                Vi: GetString(reader, ordVi),
                En: GetString(reader, ordEn),
                Role: GetString(reader, ordRole),
                Unit: GetString(reader, ordUnit),
                Notes: GetString(reader, ordNotes)));
        }

        // We return semantics without binding to a specific data result set here.
        // Binding is done by TabularSchemaBinder.
        var semantics = rows
            .Select(r => new TabularColumnSemantic(
                Name: r.ColumnName,
                Vi: string.IsNullOrWhiteSpace(r.Vi) ? null : r.Vi!.Trim(),
                En: string.IsNullOrWhiteSpace(r.En) ? null : r.En!.Trim(),
                Role: string.IsNullOrWhiteSpace(r.Role) ? null : r.Role!.Trim(),
                Unit: string.IsNullOrWhiteSpace(r.Unit) ? null : r.Unit!.Trim(),
                Notes: string.IsNullOrWhiteSpace(r.Notes) ? null : r.Notes!.Trim()))
            .ToList();

        return (semantics, true);
    }

    public static TabularSchema BindToTable(
        IReadOnlyList<TabularColumnSemantic> metadata,
        IReadOnlyList<TabularColumn> tableColumns)
    {
        metadata ??= Array.Empty<TabularColumnSemantic>();
        tableColumns ??= Array.Empty<TabularColumn>();

        var byName = new Dictionary<string, TabularColumnSemantic>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in metadata)
        {
            if (m is null || string.IsNullOrWhiteSpace(m.Name)) continue;
            // Last write wins.
            byName[m.Name.Trim()] = m;
        }

        var aligned = new List<TabularColumnSemantic>(tableColumns.Count);
        var unknown = new List<string>();

        foreach (var c in tableColumns)
        {
            if (byName.TryGetValue(c.Name, out var s))
            {
                aligned.Add(s with { Name = c.Name });
                if (IsUnknown(s)) unknown.Add(c.Name);
            }
            else
            {
                aligned.Add(new TabularColumnSemantic(Name: c.Name));
                unknown.Add(c.Name);
            }
        }

        return new TabularSchema(aligned, unknown);
    }

    private static bool IsUnknown(TabularColumnSemantic s)
        => string.IsNullOrWhiteSpace(s.Vi)
           && string.IsNullOrWhiteSpace(s.En)
           && string.IsNullOrWhiteSpace(s.Role)
           && string.IsNullOrWhiteSpace(s.Unit)
           && string.IsNullOrWhiteSpace(s.Notes);

    private static int FindOrdinal(DbDataReader reader, params string[] candidates)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            if (string.IsNullOrWhiteSpace(name))
                continue;
            foreach (var c in candidates)
            {
                if (string.Equals(name, c, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    private static object? GetObject(DbDataReader reader, int ord)
        => ord < 0 || reader.IsDBNull(ord) ? null : reader.GetValue(ord);

    private static string? GetString(DbDataReader reader, int ord)
        => GetObject(reader, ord)?.ToString();
}
