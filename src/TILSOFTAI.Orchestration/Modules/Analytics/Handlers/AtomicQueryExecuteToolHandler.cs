using System.Text.Json;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.Utilities;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Analytics.Handlers;

/// <summary>
/// Generic executor for stored procedures that follow "TILSOFTAI_sp_AtomicQuery_Template":
/// RS0 schema, RS1 summary (optional), RS2..N raw tables.
///
/// Routing rules:
/// - Prefer RS0.resultset.delivery when present: engine|display|both|auto
/// - Otherwise fallback to tableKind heuristic:
///     summary/dimension/lookup => display
///     fact/raw/bridge/timeseries => engine
/// - Fail-closed: unknown => engine
/// </summary>
public sealed class AtomicQueryExecuteToolHandler : IToolHandler
{
    public string ToolName => "atomic.query.execute";

    private readonly AtomicQueryService _atomicQueryService;
    private readonly AnalyticsService _analyticsService;
    private readonly AtomicCatalogService _catalog;

    public AtomicQueryExecuteToolHandler(
        AtomicQueryService atomicQueryService,
        AnalyticsService analyticsService,
        AtomicCatalogService catalog)
    {
        _atomicQueryService = atomicQueryService;
        _analyticsService = analyticsService;
        _catalog = catalog;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;

        var spName = NormalizeStoredProcedureName(dyn.GetStringRequired("spName"));

        // Governance: allow only stored procedures registered in catalog.
        // Fail-closed: if not present/enabled/readonly/atomicCompatible => reject.
        AtomicCatalogEntry catalogEntry;
        try
        {
            catalogEntry = await _catalog.GetRequiredAllowedAsync(spName, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            // Fallback: do not return an empty tool output (which often causes the LLM to loop).
            // Provide an actionable diagnostic so the assistant can respond to the user immediately.
            var payloadEx = new
            {
                kind = "atomic.query.execute.v1",
                schemaVersion = 2,
                generatedAtUtc = DateTimeOffset.UtcNow,
                resource = "atomic.query.execute",
                data = new
                {
                    storedProcedure = spName,
                    error = new
                    {
                        code = "catalog_not_allowed",
                        message = ex.Message
                    },
                    hint = "Register and enable this stored procedure in dbo.TILSOFTAI_SPCatalog (IsEnabled=1, IsReadOnly=1, IsAtomicCompatible=1) then retry."
                },
                warnings = new[] { "Execution was blocked by catalog governance. No DB call was made." }
            };

            var blockedExtras = new ToolDispatchExtras(
                Source: new EnvelopeSourceV1 { System = "registry", Name = "dbo.TILSOFTAI_SPCatalog", Cache = "na" },
                Evidence:
                [
                    new EnvelopeEvidenceItemV1
            {
                Id = "error",
                Type = "metric",
                Title = "Execution blocked",
                Payload = new { code = "catalog_not_allowed", message = ex.Message, spName }
            }
                ]);

            return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("atomic.query.execute blocked", payloadEx), blockedExtras);
        }
        var readOptions = new AtomicQueryReadOptions(
            MaxRowsPerTable: dyn.GetInt("maxRowsPerTable", 20000),
            MaxRowsSummary: dyn.GetInt("maxRowsSummary", 500),
            MaxSchemaRows: dyn.GetInt("maxSchemaRows", 50000),
            MaxTables: dyn.GetInt("maxTables", 20));

        var maxColumns = dyn.GetInt("maxColumns", 100);
        var previewRows = dyn.GetInt("previewRows", 100);
        var maxDisplayRows = dyn.GetInt("maxDisplayRows", 2000);

        var routing = RoutingPolicyOptions.Default with
        {
            MaxDisplayRows = Math.Clamp(maxDisplayRows, 1, readOptions.MaxRowsPerTable),
            MaxDisplayColumns = Math.Clamp(maxColumns, 1, 500)
        };

        var parameters = ParseSqlParameters(dyn.GetJson("params"));

        // Filter params by catalog allow-list (Option 2): drop unknown params and emit warnings.
        var allowedParams = AtomicCatalogService.GetAllowedParamNames(catalogEntry.ParamsJson);
        var (filteredParams, droppedParams) = FilterParams(parameters, allowedParams);

        // Normalize semantic parameters (e.g., Season) to improve compatibility with ERP conventions.
        // This runs after governance filtering and before executing the stored procedure.
        filteredParams = NormalizeSeasonParams(filteredParams);

        var atomic = await _atomicQueryService.ExecuteAsync(spName, filteredParams, readOptions, cancellationToken);

        // Summary (RS1) is always safe to display (bounded by MaxRowsSummary).
        var summaryPayload = atomic.Summary is null ? null : new
        {
            index = atomic.Summary.Schema.Index,
            tableName = atomic.Summary.Schema.TableName,
            tableKind = atomic.Summary.Schema.TableKind,
            schema = atomic.Summary.Schema,
            table = TrimColumns(atomic.Summary.Table, maxColumns)
        };

        var displayTables = new List<object>();
        var engineDatasets = new List<object>();
        var warnings = new List<string>();

        if (allowedParams.Count == 0)
            warnings.Add($"Catalog allow-list for '{spName}' is empty/missing. Parameters were accepted, but unknown parameters may be ignored using SQL metadata. Populate ParamsJson in dbo.TILSOFTAI_SPCatalog for stronger governance and better LLM guidance.");

        if (droppedParams.Count > 0)
            warnings.Add($"Dropped {droppedParams.Count} unknown params (not in catalog allow-list) for '{spName}': {string.Join(", ", droppedParams)}");

        foreach (var t in atomic.Tables)
        {
            var trimmedTable = TrimColumns(t.Table, maxColumns);
            var stats = TableStats.From(trimmedTable);
            var decision = DecideDelivery(t.Schema, stats, routing);

            if (decision.Display)
            {
                var displayTable = TrimRows(trimmedTable, routing.MaxDisplayRows);
                var displayTrunc = new
                {
                    rows = new { returned = displayTable.Rows.Count, max = routing.MaxDisplayRows, original = trimmedTable.Rows.Count },
                    columns = new { returned = displayTable.Columns.Count, max = maxColumns, original = t.Table.Columns.Count }
                };

                displayTables.Add(new
                {
                    index = t.Schema.Index,
                    tableName = t.Schema.TableName,
                    tableKind = t.Schema.TableKind,
                    delivery = t.Schema.Delivery ?? "auto",
                    grain = t.Schema.Grain,
                    primaryKey = t.Schema.PrimaryKey,
                    joinHints = t.Schema.JoinHints,
                    description_vi = t.Schema.DescriptionVi,
                    description_en = t.Schema.DescriptionEn,
                    schema = t.Schema,
                    routingReason = decision.Reason,
                    truncation = displayTrunc,
                    table = displayTable
                });

                if (displayTable.Rows.Count < trimmedTable.Rows.Count)
                    warnings.Add($"Display table '{t.Schema.TableName}' was truncated to {displayTable.Rows.Count} rows (maxDisplayRows={routing.MaxDisplayRows}). Use engineDatasets + analytics.run for full analysis.");
            }

            if (decision.Engine)
            {
                // Create short-lived dataset from table for AtomicDataEngine.
                var bounds = new AnalyticsService.DatasetBounds(
                    MaxRows: Math.Min(trimmedTable.Rows.Count, readOptions.MaxRowsPerTable),
                    MaxColumns: maxColumns,
                    PreviewRows: previewRows);

                var dataset = await _analyticsService.CreateDatasetFromTabularAsync(
                    source: "atomic",
                    tabular: trimmedTable,
                    bounds: bounds,
                    context: context,
                    cancellationToken: cancellationToken);

                engineDatasets.Add(new
                {
                    index = t.Schema.Index,
                    tableName = t.Schema.TableName,
                    tableKind = t.Schema.TableKind,
                    delivery = t.Schema.Delivery ?? "auto",
                    routingReason = decision.Reason,
                    datasetId = dataset.DatasetId,
                    expiresAtUtc = dataset.ExpiresAtUtc,
                    semanticSchema = t.Schema,
                    dataSchema = dataset.Schema,
                    preview = new
                    {
                        columns = dataset.Schema.Select(c => c.Name),
                        rows = dataset.Preview
                    }
                });
            }
        }

        if (displayTables.Count == 0 && engineDatasets.Count == 0)
            warnings.Add("No data tables returned (RS2..RSN). Ensure RS0 declares at least one non-summary result set.");

        // Protect against accidental token bloat.
        if (displayTables.Count > 0)
            warnings.Add("Display tables are bounded. For large analysis prefer engineDatasets + analytics.run.");

        var payload = new
        {
            kind = "atomic.query.execute.v1",
            schemaVersion = 2,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "atomic.query.execute",
            data = new
            {
                storedProcedure = spName,
                catalog = new
                {
                    domain = catalogEntry.Domain,
                    entity = catalogEntry.Entity,
                    intent = new { vi = catalogEntry.IntentVi, en = catalogEntry.IntentEn },
                    tags = catalogEntry.Tags
                },
                schema = atomic.Schema,
                summary = summaryPayload,
                displayTables,
                engineDatasets
            },
            warnings = warnings.ToArray()
        };

        // Evidence: provide a compact, stable slice so the client can always render an answer.
        var evidence = BuildEvidenceFromAtomicResult(spName, atomic, displayTables.Count, engineDatasets.Count);

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1 { System = "sqlserver", Name = spName, Cache = "na" },
            Evidence: evidence);

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("atomic.query.execute executed", payload), extras);
    }

    private static IReadOnlyList<EnvelopeEvidenceItemV1> BuildEvidenceFromAtomicResult(
        string spName,
        AtomicQueryResult atomic,
        int displayTableCount,
        int engineDatasetCount)
    {
        var list = new List<EnvelopeEvidenceItemV1>();

        // 1) Prefer a "totalCount" metric from RS1 summary if present.
        var totalCount = TryGetIntFromSummary(atomic.Summary?.Table, "totalCount");
        if (totalCount is not null)
        {
            list.Add(new EnvelopeEvidenceItemV1
            {
                Id = "total_count",
                Type = "metric",
                Title = "Total count",
                Payload = new
                {
                    spName,
                    totalCount,
                    filters = new
                    {
                        season = TryGetStringFromSummary(atomic.Summary?.Table, "seasonFilter"),
                        collection = TryGetStringFromSummary(atomic.Summary?.Table, "collectionFilter"),
                        rangeName = TryGetStringFromSummary(atomic.Summary?.Table, "rangeNameFilter")
                    }
                }
            });
        }

        // 2) Always return a minimal execution snapshot.
        list.Add(new EnvelopeEvidenceItemV1
        {
            Id = "execution",
            Type = "metric",
            Title = "Execution snapshot",
            Payload = new
            {
                spName,
                resultSets = new
                {
                    summary = atomic.Summary is not null,
                    tables = atomic.Tables.Count,
                    displayTables = displayTableCount,
                    engineDatasets = engineDatasetCount
                }
            }
        });

        // 3) If summary is missing but we have at least one table, expose a small preview hint.
        if (atomic.Summary is null && atomic.Tables.Count > 0)
        {
            var first = atomic.Tables[0];
            list.Add(new EnvelopeEvidenceItemV1
            {
                Id = "preview_hint",
                Type = "list",
                Title = "Preview hint",
                Payload = new
                {
                    tableName = first.Schema.TableName,
                    columns = first.Table.Columns.Select(c => c.Name).Take(20),
                    rowsReturned = first.Table.Rows.Count,
                    note = "Summary RS1 was not present; use displayTables/engineDatasets in payload.data for details."
                }
            });
        }

        return list;
    }

    private static int? TryGetIntFromSummary(TabularData? summary, string columnName)
    {
        if (summary is null || summary.Rows.Count == 0)
            return null;

        var idx = FindColumnIndex(summary, columnName);
        if (idx < 0)
            return null;

        var v = summary.Rows[0][idx];
        if (v is null)
            return null;

        if (v is int i) return i;
        if (v is long l) return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
        if (v is decimal d) return (int)d;
        if (v is double db) return (int)db;

        if (int.TryParse(v.ToString(), out var parsed))
            return parsed;

        return null;
    }

    private static string? TryGetStringFromSummary(TabularData? summary, string columnName)
    {
        if (summary is null || summary.Rows.Count == 0)
            return null;

        var idx = FindColumnIndex(summary, columnName);
        if (idx < 0)
            return null;

        return summary.Rows[0][idx]?.ToString();
    }

    private static int FindColumnIndex(TabularData summary, string columnName)
    {
        for (var i = 0; i < summary.Columns.Count; i++)
        {
            if (string.Equals(summary.Columns[i].Name, columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    private static string NormalizeStoredProcedureName(string input)
    {
        var sp = input.Trim();
        if (!sp.Contains('.', StringComparison.Ordinal))
            sp = "dbo." + sp;

        // Fail-closed: allow only dbo.<identifier> and must start with dbo.TILSOFTAI_sp_
        if (!sp.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("spName must be under schema 'dbo'.");

        if (!sp.StartsWith("dbo.TILSOFTAI_sp_", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("spName must start with dbo.TILSOFTAI_sp_.");

        // defensive: restrict characters
        for (var i = 0; i < sp.Length; i++)
        {
            var ch = sp[i];
            var ok = char.IsLetterOrDigit(ch) || ch == '_' || ch == '.';
            if (!ok)
                throw new ArgumentException("spName contains invalid characters.");
        }

        return sp;
    }

    private static IReadOnlyDictionary<string, object?> ParseSqlParameters(JsonElement? je)
    {
        if (je is null || je.Value.ValueKind == JsonValueKind.Undefined || je.Value.ValueKind == JsonValueKind.Null)
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (je.Value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("params must be a JSON object.");

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in je.Value.EnumerateObject())
        {
            dict[prop.Name] = ConvertJsonValue(prop.Value);
        }

        return dict;
    }

    private static (IReadOnlyDictionary<string, object?> Filtered, List<string> Dropped) FilterParams(
        IReadOnlyDictionary<string, object?> parameters,
        IReadOnlySet<string> allowedParams)
    {
        if (parameters is null || parameters.Count == 0)
            return (new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase), new List<string>());

        // If the catalog allow-list is empty/missing, do NOT drop everything.
        // We keep the provided params and rely on DB metadata filtering at execution time (see AtomicQueryRepository).
        if (allowedParams is null || allowedParams.Count == 0)
        {
            var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in parameters)
            {
                var name = kv.Key;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!name.StartsWith("@", StringComparison.Ordinal))
                    name = "@" + name;

                normalized[name] = kv.Value;
            }
            return (normalized, new List<string>());
        }

        var filtered = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var dropped = new List<string>();

        foreach (var kv in parameters)
        {
            var normalized = AtomicCatalogService.NormalizeParamName(kv.Key);
            if (allowedParams.Contains(normalized))
            {
                // Keep original key; repository will normalize to @param.
                filtered[kv.Key] = kv.Value;
            }
            else
            {
                dropped.Add(kv.Key);
            }
        }

        return (filtered, dropped);
    }

    /// <summary>
    /// Normalizes common semantic parameters to ERP-friendly canonical forms.
    /// Currently supports Season: "24/25" => "2024/2025".
    ///
    /// This runs after governance filtering and before execution.
    /// It does not inject new params; it only normalizes existing ones.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> NormalizeSeasonParams(IReadOnlyDictionary<string, object?> parameters)
    {
        if (parameters is null || parameters.Count == 0)
            return parameters;

        // Clone into a mutable dictionary.
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in parameters)
            dict[kv.Key] = kv.Value;

        foreach (var key in dict.Keys.ToList())
        {
            var normalizedName = AtomicCatalogService.NormalizeParamName(key);
            if (!string.Equals(normalizedName, "@Season", StringComparison.OrdinalIgnoreCase))
                continue;

            var v = dict[key];
            if (v is null)
                continue;

            var s = v.ToString();
            if (string.IsNullOrWhiteSpace(s))
                continue;

            var normalizedSeason = SeasonNormalizer.NormalizeValue(s);
            if (!string.Equals(normalizedSeason, s, StringComparison.Ordinal))
                dict[key] = normalizedSeason;
        }

        return dict;
    }

    private static object? ConvertJsonValue(JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return null;

            case JsonValueKind.String:
                return v.GetString();

            case JsonValueKind.True:
            case JsonValueKind.False:
                return v.GetBoolean();

            case JsonValueKind.Number:
                // Prefer int when possible, else long, else decimal.
                if (v.TryGetInt32(out var i)) return i;
                if (v.TryGetInt64(out var l)) return l;
                if (v.TryGetDecimal(out var d)) return d;
                return v.GetDouble();

            case JsonValueKind.Object:
            case JsonValueKind.Array:
                // Pass JSON payload as string; SQL can accept NVARCHAR(MAX) JSON if needed.
                return v.GetRawText();

            default:
                return v.GetRawText();
        }
    }

    private static RoutingDecision DecideDelivery(AtomicResultSetSchema schema, TableStats stats, RoutingPolicyOptions policy)
    {
        var delivery = (schema.Delivery ?? "auto").Trim().ToLowerInvariant();
        var kind = (schema.TableKind ?? string.Empty).Trim().ToLowerInvariant();

        var sizeTooLargeForDisplay =
            stats.RowCount > policy.HardMaxDisplayRows ||
            stats.ColCount > policy.MaxDisplayColumns ||
            stats.CellCount > policy.MaxDisplayCells;

        // 1) Explicit delivery directive in RS0 (preferred)
        if (delivery == "engine")
            return new RoutingDecision(Engine: true, Display: false, Reason: "RS0.delivery=engine");

        if (delivery == "display")
        {
            // Guardrail: if too large, convert to BOTH so user can still analyze.
            if (sizeTooLargeForDisplay)
                return new RoutingDecision(Engine: true, Display: true, Reason: "RS0.delivery=display but table is large => both (display truncated + engine dataset)");
            return new RoutingDecision(Engine: false, Display: true, Reason: "RS0.delivery=display");
        }

        if (delivery == "both")
            return new RoutingDecision(Engine: true, Display: true, Reason: "RS0.delivery=both");

        // 2) Auto routing by tableKind + size heuristics
        var displayPreferred = kind is "summary" or "dimension" or "lookup" or "kpi" or "report";
        var enginePreferred = kind is "fact" or "raw" or "bridge" or "timeseries" or "transaction";

        if (displayPreferred)
        {
            if (sizeTooLargeForDisplay)
                return new RoutingDecision(Engine: true, Display: true, Reason: $"tableKind={kind} display-preferred but large => both (display truncated + engine dataset)");
            return new RoutingDecision(Engine: false, Display: true, Reason: $"tableKind={kind} => display");
        }

        if (enginePreferred)
        {
            // If small, show + also create dataset (helps debugging and UX)
            if (stats.RowCount <= policy.SmallTableRowsForBoth && stats.CellCount <= policy.SmallTableCellsForBoth)
                return new RoutingDecision(Engine: true, Display: true, Reason: $"tableKind={kind} engine-preferred but small => both");

            return new RoutingDecision(Engine: true, Display: false, Reason: $"tableKind={kind} => engine");
        }

        // 3) Unknown kinds: fail-closed to engine; allow BOTH only when table is tiny.
        if (stats.RowCount <= policy.TinyTableRowsForBoth && stats.CellCount <= policy.TinyTableCellsForBoth)
            return new RoutingDecision(Engine: true, Display: true, Reason: "unknown tableKind but tiny => both");

        return new RoutingDecision(Engine: true, Display: false, Reason: "unknown tableKind => engine (fail-closed)");
    }

    private static TabularData TrimRows(TabularData table, int maxRows)
    {
        maxRows = Math.Clamp(maxRows, 0, 200000);
        if (maxRows == 0 || table.Rows.Count <= maxRows)
            return table;

        var rows = table.Rows.Take(maxRows).ToArray();
        return new TabularData(table.Columns, rows, table.TotalCount);
    }

    private sealed record RoutingDecision(bool Engine, bool Display, string Reason);

    private sealed record RoutingPolicyOptions(
        int MaxDisplayRows,
        int HardMaxDisplayRows,
        int MaxDisplayColumns,
        long MaxDisplayCells,
        int SmallTableRowsForBoth,
        long SmallTableCellsForBoth,
        int TinyTableRowsForBoth,
        long TinyTableCellsForBoth)
    {
        public static RoutingPolicyOptions Default => new(
            MaxDisplayRows: 2000,
            HardMaxDisplayRows: 5000,
            MaxDisplayColumns: 60,
            MaxDisplayCells: 120_000,
            SmallTableRowsForBoth: 250,
            SmallTableCellsForBoth: 25_000,
            TinyTableRowsForBoth: 50,
            TinyTableCellsForBoth: 5_000);
    }

    private sealed record TableStats(int RowCount, int ColCount, long CellCount)
    {
        public static TableStats From(TabularData table)
        {
            var rows = table.Rows.Count;
            var cols = table.Columns.Count;
            return new TableStats(rows, cols, (long)rows * cols);
        }
    }

    private static TabularData TrimColumns(TabularData table, int maxColumns)
    {
        maxColumns = Math.Clamp(maxColumns, 1, 500);
        if (table.Columns.Count <= maxColumns)
            return table;

        var keep = table.Columns.Take(maxColumns).ToArray();
        var keptIdx = Enumerable.Range(0, maxColumns).ToArray();

        var rows = new object?[table.Rows.Count][];
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var src = table.Rows[r];
            var dst = new object?[maxColumns];
            for (var c = 0; c < maxColumns; c++)
                dst[c] = src[keptIdx[c]];
            rows[r] = dst;
        }

        return new TabularData(keep, rows, table.TotalCount);
    }


}
