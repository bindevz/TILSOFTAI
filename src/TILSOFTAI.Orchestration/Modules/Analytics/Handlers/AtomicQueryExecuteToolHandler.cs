using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Configuration;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Chat.Localization;
using TILSOFTAI.Orchestration.Formatting;
using TILSOFTAI.Orchestration.SK;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Analytics.Handlers;

/// <summary>
/// Generic executor for stored procedures that follow "TILSOFTAI_sp_AtomicQuery_Template":
/// RS0 schema, RS1 summary (optional), RS2..N raw tables.
///
/// Routing rules:
/// - Prefer RS0.resultset delivery/datasetName when present: engine|display|both
/// - Fallback: SPCatalog.SchemaHintsJson.resultSets (rsIndex -> delivery/datasetName/tableKind)
/// - If neither present, fail fast with SCHEMA_METADATA_REQUIRED
/// </summary>
public sealed class AtomicQueryExecuteToolHandler : IToolHandler
{
    public string ToolName => "atomic.query.execute";


    private readonly AtomicQueryService _atomicQueryService;
    private readonly AnalyticsService _analyticsService;
    private readonly AtomicCatalogService _catalog;
    private readonly ExecutionContextAccessor _ctxAccessor;
    private readonly AppSettings _settings;
    private readonly IChatTextLocalizer _localizer;
    private readonly ILogger<AtomicQueryExecuteToolHandler> _logger;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AtomicQueryExecuteToolHandler(
        AtomicQueryService atomicQueryService,
        AnalyticsService analyticsService,
        AtomicCatalogService catalog,
        ExecutionContextAccessor ctxAccessor,
        IChatTextLocalizer localizer,
        IOptions<AppSettings> settings,
        ILogger<AtomicQueryExecuteToolHandler> logger)
    {
        _atomicQueryService = atomicQueryService;
        _analyticsService = analyticsService;
        _catalog = catalog;
        _ctxAccessor = ctxAccessor;
        _localizer = localizer;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;

        var spName = NormalizeStoredProcedureName(dyn.GetStringRequired("spName"));
        _logger.LogInformation("AtomicQueryExecute start sp={Sp} req={RequestId} trace={TraceId}", spName, context.RequestId, context.TraceId);

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
            MaxDisplayRows = Math.Clamp(maxDisplayRows, 1, readOptions.MaxRowsPerTable)
        };

        var (parameters, normalizedKeys) = ParseSqlParameters(dyn.GetJson("params"));

        // Filter params by catalog allow-list (Option 2): drop unknown params and emit warnings.
        var allowedParams = AtomicCatalogService.GetAllowedParamNames(catalogEntry.ParamsJson);
        var defaultParams = AtomicCatalogService.GetDefaultParamValues(catalogEntry.ParamsJson);
        var (filteredParams, droppedParams) = FilterParams(parameters, allowedParams);

        // Strict parameter contract: the caller MUST use exactly the parameter names declared in ParamsJson.
        // If unknown keys are provided, do not execute SQL (fail-fast) so the LLM can correct its call deterministically.
        if (allowedParams is null || allowedParams.Count == 0)
        {
            var expected = Array.Empty<string>();
            var payloadMissing = BuildStrictParamFailurePayload(
                spName,
                catalogEntry,
                code: "missing_params_contract",
                message: "ParamsJson is missing/empty for this stored procedure. Cannot validate input parameters.",
                expectedParams: expected,
                receivedParams: parameters.Keys.ToArray());

            var extrasMissing = new ToolDispatchExtras(
                Source: new EnvelopeSourceV1 { System = "registry", Name = "dbo.TILSOFTAI_SPCatalog", Cache = "na" },
                Evidence: new[] { new EnvelopeEvidenceItemV1 { Id = "param_contract_missing", Type = "metric", Title = "Parameter contract missing", Payload = new { spName, code = "missing_params_contract" } } });

            return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateFailure("atomic.query.execute blocked: ParamsJson missing", payloadMissing), extrasMissing);
        }

        if (droppedParams.Count > 0)
        {
            var expected = FormatExpectedParamsForHumans(allowedParams);
            var payloadInvalid = BuildStrictParamFailurePayload(
                spName,
                catalogEntry,
                code: "invalid_parameters",
                message: "Unknown parameter keys were provided. Use only parameter names declared in ParamsJson (atomic.catalog.search results[].parameters).",
                expectedParams: expected,
                receivedParams: parameters.Keys.ToArray());

            var extrasInvalid = new ToolDispatchExtras(
                Source: new EnvelopeSourceV1 { System = "registry", Name = "dbo.TILSOFTAI_SPCatalog", Cache = "na" },
                Evidence: new[] { new EnvelopeEvidenceItemV1 { Id = "invalid_parameters", Type = "metric", Title = "Invalid parameters", Payload = new { spName, expected = expected, received = parameters.Keys.ToArray(), dropped = droppedParams } } });

            return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateFailure("atomic.query.execute blocked: invalid parameters", payloadInvalid), extrasInvalid);
        }

        // Parameter normalization here is limited to canonicalizing keys (trim + '@' prefix).
        // The LLM must still follow ParamsJson for allowed filters and values.
        var normalizedParams = ApplyDefaults(filteredParams, defaultParams, allowedParams);

        var atomic = await _atomicQueryService.ExecuteAsync(spName, normalizedParams, readOptions, cancellationToken);

        // Summary (RS1) is always safe to display (bounded by MaxRowsSummary).
        var summaryPayload = atomic.Summary is null ? null : new
        {
            index = atomic.Summary.Schema.Index,
            tableName = atomic.Summary.Schema.TableName,
            tableKind = atomic.Summary.Schema.TableKind,
            schema = atomic.Summary.Schema,
            table = TrimColumns(atomic.Summary.Table, maxColumns),
            effectiveParams = BuildEffectiveParamsEcho(normalizedParams)
        };

        var displayTables = new List<object>();
        var engineDatasets = new List<object>();
        var warnings = new List<string>();
        if (normalizedKeys.Count > 0)
        {
            var preview = normalizedKeys.Take(5).ToArray();
            var suffix = normalizedKeys.Count > 5 ? ", ..." : string.Empty;
            warnings.Add($"Parameter keys normalized: {string.Join(", ", preview)}{suffix}");
        }
        var schemaHints = ParseSchemaHints(catalogEntry.SchemaHintsJson);
        var schemaHintsByTable = schemaHints.Tables;
        var schemaHintsByResultSet = schemaHints.ResultSets;
        var schemaDigestTables = new List<object>();
        var engineDatasetDigests = new List<object>();
        TabularData? listPreviewTable = null;

        var resolvedTables = new List<(AtomicResultSet Table, ResultSetMetadata Meta)>();
        var missingMetadata = new List<object>();

        foreach (var t in atomic.Tables)
        {
            if (TryResolveResultSetMeta(t.Schema, schemaHintsByResultSet, out var meta))
            {
                resolvedTables.Add((t, meta));
            }
            else
            {
                missingMetadata.Add(new
                {
                    rsIndex = t.Schema.Index,
                    tableName = t.Schema.TableName,
                    delivery = t.Schema.Delivery
                });
            }
        }

        if (missingMetadata.Count > 0)
        {
            var missingMetaMessage = _localizer.Get(ChatTextKeys.ErrorSchemaMetadataRequired);
            var payloadMissingMeta = BuildSchemaMetadataFailurePayload(spName, catalogEntry, missingMetadata, missingMetaMessage);
            var extrasMissingMeta = new ToolDispatchExtras(
                Source: new EnvelopeSourceV1 { System = "registry", Name = "dbo.TILSOFTAI_SPCatalog", Cache = "na" },
                Evidence: new[]
                {
                    new EnvelopeEvidenceItemV1
                    {
                        Id = "schema_metadata_required",
                        Type = "metric",
                        Title = missingMetaMessage,
                        Payload = new { spName, missing = missingMetadata }
                    }
                });

            return ToolDispatchResultFactory.Create(
                dyn,
                ToolExecutionResult.CreateFailure("atomic.query.execute blocked: schema metadata required", payloadMissingMeta),
                extrasMissingMeta);
        }

        foreach (var (t, meta) in resolvedTables)
        {
            var trimmedTable = TrimColumns(t.Table, maxColumns);
            var decision = ResolveDelivery(meta);

            var digestColumns = BuildColumnDigests(t.Schema, trimmedTable, Math.Min(maxColumns, 60));
            schemaHintsByTable.TryGetValue(meta.TableName, out var tableHints);
            var primaryKey = ResolvePrimaryKey(t.Schema, tableHints);
            var joinHints = ResolveJoinHints(t.Schema, tableHints, warnings);
            var foreignKeys = ResolveForeignKeys(tableHints, joinHints, primaryKey);
            var measures = ResolveMeasureHints(t.Schema, tableHints);
            var dimensions = ResolveDimensionHints(t.Schema, tableHints);

            schemaDigestTables.Add(new
            {
                name = meta.TableName,
                kind = decision.Engine && decision.Display ? "both" : decision.Engine ? "engine" : "display",
                rowCountEstimate = trimmedTable.TotalCount ?? trimmedTable.Rows.Count,
                primaryKey,
                foreignKeys,
                joinHints,
                measures,
                dimensions,
                columns = digestColumns
            });

            if (decision.Display)
            {
                if (listPreviewTable is null && trimmedTable.Rows.Count > 0)
                {
                    listPreviewTable = trimmedTable;
                }

                var displayTrunc = new
                {
                    rows = new { returned = Math.Min(trimmedTable.Rows.Count, routing.MaxDisplayRows), max = routing.MaxDisplayRows, original = trimmedTable.Rows.Count },
                    columns = new { returned = trimmedTable.Columns.Count, max = maxColumns, original = t.Table.Columns.Count }
                };

                var displayPayload = new TabularData(trimmedTable.Columns, Array.Empty<object?[]>(), trimmedTable.TotalCount);

                displayTables.Add(new
                {
                    index = t.Schema.Index,
                    tableName = meta.TableName,
                    tableKind = meta.TableKind,
                    delivery = meta.Delivery,
                    grain = t.Schema.Grain,
                    primaryKey,
                    joinHints,
                    description_vi = t.Schema.DescriptionVi,
                    description_en = t.Schema.DescriptionEn,
                    schema = t.Schema,
                    routingReason = decision.Reason,
                    truncation = displayTrunc,
                    table = displayPayload,
                    rowsReturned = trimmedTable.Rows.Count,
                    columnCount = trimmedTable.Columns.Count
                });

                if (trimmedTable.Rows.Count > routing.MaxDisplayRows)
                    warnings.Add($"Display table '{meta.TableName}' was truncated to {routing.MaxDisplayRows} rows (maxDisplayRows={routing.MaxDisplayRows}). Use engineDatasets + analytics.run for full analysis.");
            }

            if (decision.Engine)
            {
                // Do NOT create an engine dataset from an empty table.
                // This often occurs when a stored procedure returns a schema-only RS (e.g., list mode) and can mislead the LLM into retry loops.
                if (trimmedTable.Rows.Count == 0)
                {
                    warnings.Add($"Engine table '{meta.TableName}' returned 0 rows; dataset was not created. If you intended dataset mode, pass @Page=0 (and ensure @Page/@Size are not dropped by catalog allow-lists).");
                }
                else
                {
                    // Create short-lived dataset from table for AtomicDataEngine.
                    var bounds = new AnalyticsService.DatasetBounds(
                        MaxRows: Math.Min(trimmedTable.Rows.Count, readOptions.MaxRowsPerTable),
                        MaxColumns: maxColumns,
                        PreviewRows: previewRows);

                    Func<string, object?> digestFactory = datasetId => BuildEngineDatasetDigest(
                        datasetId,
                        meta.TableName,
                        digestColumns,
                        primaryKey,
                        joinHints,
                        measures,
                        dimensions);

                    var dataset = await _analyticsService.CreateDatasetFromTabularAsync(
                        source: "atomic",
                        tabular: trimmedTable,
                        bounds: bounds,
                        context: context,
                        cancellationToken: cancellationToken,
                        schemaDigestFactory: digestFactory);

                    engineDatasetDigests.Add(digestFactory(dataset.DatasetId));

                    engineDatasets.Add(new
                    {
                        index = t.Schema.Index,
                        tableName = meta.TableName,
                        tableKind = meta.TableKind,
                        delivery = meta.Delivery,
                        routingReason = decision.Reason,
                        datasetId = dataset.DatasetId,
                        expiresAtUtc = dataset.ExpiresAtUtc,
                        semanticSchema = t.Schema,
                        dataSchema = dataset.Schema,
                        preview = new
                        {
                            columns = dataset.Schema.Select(c => c.Name),
                            rows = Array.Empty<object?[]>()
                        }
                    });
                }
            }

        }

        if (displayTables.Count == 0 && engineDatasets.Count == 0)
            warnings.Add("No data tables returned (RS2..RSN). Ensure RS0 declares at least one non-summary result set.");

        // Protect against accidental token bloat.
        if (displayTables.Count > 0)
            warnings.Add("Display tables are bounded. For large analysis prefer engineDatasets + analytics.run.");

        if (listPreviewTable is not null)
        {
            var previewRowLimit = Math.Clamp(_settings.AnalyticsEngine.PreviewRowLimit, 1, 200);
            var renderOptions = new MarkdownTableRenderOptions { MaxRows = previewRowLimit };
            _ctxAccessor.LastListPreviewMarkdown = MarkdownTableRenderer.Render(listPreviewTable, renderOptions);
        }

        var schemaDigest = new
        {
            tables = schemaDigestTables
        };

        var engineDatasetsDigest = new
        {
            datasets = engineDatasetDigests
        };

        _ctxAccessor.LastSchemaDigest = JsonSerializer.Serialize(schemaDigest, Json);
        _ctxAccessor.LastDatasetDigest = JsonSerializer.Serialize(engineDatasetsDigest, Json);

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
        var evidence = BuildEvidenceFromAtomicResult(spName, atomic, displayTables.Count, engineDatasets.Count, schemaDigest, engineDatasetsDigest, warnings);

        var totalCount = TryGetIntFromSummary(atomic.Summary?.Table, "totalCount");
        _logger.LogInformation(
            "AtomicQueryExecute done sp={Sp} totalCount={TotalCount} displayTables={DisplayTables} engineDatasets={EngineDatasets} tables={Tables}",
            spName,
            totalCount,
            displayTables.Count,
            engineDatasets.Count,
            atomic.Tables.Count);

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1 { System = "sqlserver", Name = spName, Cache = "na" },
            Evidence: evidence);

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("atomic.query.execute executed", payload), extras);
    }

    private static IReadOnlyList<EnvelopeEvidenceItemV1> BuildEvidenceFromAtomicResult(
        string spName,
        AtomicQueryResult atomic,
        int displayTableCount,
        int engineDatasetCount,
        object schemaDigest,
        object engineDatasetsDigest,
        IReadOnlyList<string> warnings)
    {
        var list = new List<EnvelopeEvidenceItemV1>();

        // 1) Prefer a "totalCount" metric from RS1 summary if present.
        var totalCount = TryGetIntFromSummary(atomic.Summary?.Table, "totalCount");
        if (totalCount is not null)
        {
            var filters = BuildFiltersFromSummary(atomic);
            list.Add(new EnvelopeEvidenceItemV1
            {
                Id = "total_count",
                Type = "metric",
                Title = "Total count",
                Payload = new
                {
                    spName,
                    totalCount,
                    filters
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

        list.Add(new EnvelopeEvidenceItemV1
        {
            Id = "schema_digest",
            Type = "entity",
            Title = "Schema digest",
            Payload = schemaDigest
        });

        list.Add(new EnvelopeEvidenceItemV1
        {
            Id = "engine_datasets",
            Type = "list",
            Title = "Engine datasets",
            Payload = engineDatasetsDigest
        });

        if (warnings is not null && warnings.Count > 0)
        {
            list.Add(new EnvelopeEvidenceItemV1
            {
                Id = "warnings",
                Type = "metric",
                Title = "Warnings",
                Payload = new { warnings = warnings.ToArray() }
            });
        }

        return list;
    }

    private static IReadOnlyList<object> BuildColumnDigests(AtomicResultSetSchema schema, TabularData table, int maxColumns)
    {
        maxColumns = Math.Clamp(maxColumns, 1, 200);
        var list = new List<object>(maxColumns);

        if (schema.Columns is not null && schema.Columns.Count > 0)
        {
            foreach (var c in schema.Columns.Take(maxColumns))
            {
                list.Add(new
                {
                    name = c.Name,
                    sqlType = c.SqlType,
                    tabularType = c.TabularType,
                    semanticType = c.SemanticType,
                    role = c.Role,
                    nullable = c.Nullable
                });
            }
            return list;
        }

        foreach (var c in table.Columns.Take(maxColumns))
        {
            list.Add(new
            {
                name = c.Name,
                sqlType = (string?)null,
                tabularType = c.Type.ToString(),
                semanticType = (string?)null,
                role = (string?)null,
                nullable = (bool?)null
            });
        }

        return list;
    }

    private sealed record SchemaHintsForeignKey(string Column, string? RefTable, string? RefColumn);

    private sealed record SchemaHintsTable(
        IReadOnlyList<string> PrimaryKey,
        IReadOnlyList<SchemaHintsForeignKey> ForeignKeys,
        IReadOnlyList<string> MeasureHints,
        IReadOnlyList<string> DimensionHints);

    private sealed record SchemaHintsResultSet(
        int Index,
        string? DatasetName,
        string? Delivery,
        string? TableKind);

    private sealed record SchemaHints(
        IReadOnlyDictionary<string, SchemaHintsTable> Tables,
        IReadOnlyDictionary<int, SchemaHintsResultSet> ResultSets);

    private sealed record ResultSetMetadata(
        int Index,
        string TableName,
        string? TableKind,
        string Delivery,
        string DeliverySource);

    private static SchemaHints ParseSchemaHints(string? schemaHintsJson)
    {
        var tables = new Dictionary<string, SchemaHintsTable>(StringComparer.OrdinalIgnoreCase);
        var resultSets = new Dictionary<int, SchemaHintsResultSet>();
        if (string.IsNullOrWhiteSpace(schemaHintsJson))
            return new SchemaHints(tables, resultSets);

        try
        {
            using var doc = JsonDocument.Parse(schemaHintsJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new SchemaHints(tables, resultSets);

            if (root.TryGetProperty("tables", out var tablesEl) && tablesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tablesEl.EnumerateArray())
                {
                    if (t.ValueKind != JsonValueKind.Object)
                        continue;

                    var tableName = GetString(t, "tableName");
                    if (string.IsNullOrWhiteSpace(tableName))
                        continue;

                    var primaryKey = ReadStringArray(t, "primaryKey");
                    var measureHints = ReadStringArray(t, "measureHints");
                    var dimensionHints = ReadStringArray(t, "dimensionHints");
                    var foreignKeys = ReadForeignKeys(t);

                    tables[tableName!] = new SchemaHintsTable(primaryKey, foreignKeys, measureHints, dimensionHints);
                }
            }

            if (root.TryGetProperty("resultSets", out var resultSetsEl) && resultSetsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var rs in resultSetsEl.EnumerateArray())
                {
                    if (rs.ValueKind != JsonValueKind.Object)
                        continue;

                    var rsIndex =
                        GetInt(rs, "rsIndex") ??
                        GetInt(rs, "resultSetIndex") ??
                        GetInt(rs, "resultSet") ??
                        GetInt(rs, "index");

                    if (rsIndex is null || rsIndex <= 0)
                        continue;

                    var datasetName = GetStringAny(rs, "datasetName", "tableName", "name");
                    var delivery = GetStringAny(rs, "delivery", "target", "audience");
                    var tableKind = GetStringAny(rs, "tableKind", "kind");

                    resultSets[rsIndex.Value] = new SchemaHintsResultSet(rsIndex.Value, datasetName, delivery, tableKind);
                }
            }
        }
        catch
        {
            // Ignore schema hints parse errors.
        }

        return new SchemaHints(tables, resultSets);
    }

    private static IReadOnlyList<SchemaHintsForeignKey> ReadForeignKeys(JsonElement table)
    {
        var list = new List<SchemaHintsForeignKey>();
        if (!table.TryGetProperty("foreignKeys", out var fkEl) || fkEl.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var fk in fkEl.EnumerateArray())
        {
            if (fk.ValueKind != JsonValueKind.Object)
                continue;

            var column = GetString(fk, "column");
            var refTable = GetString(fk, "refTable");
            var refColumn = GetString(fk, "refColumn");

            if (string.IsNullOrWhiteSpace(column))
                continue;

            list.Add(new SchemaHintsForeignKey(column!, refTable, refColumn));
        }

        return list;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var v in arr.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.String)
                continue;

            var s = v.GetString();
            if (string.IsNullOrWhiteSpace(s))
                continue;

            list.Add(s.Trim());
        }

        return list;
    }

    private static string? GetString(JsonElement node, string propertyName)
        => node.TryGetProperty(propertyName, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? GetStringAny(JsonElement node, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            var value = GetString(node, name);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static int? GetInt(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var v))
            return null;

        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt32(out var n) => n,
            JsonValueKind.String when int.TryParse(v.GetString(), out var s) => s,
            _ => null
        };
    }

    private static bool TryResolveResultSetMeta(
        AtomicResultSetSchema schema,
        IReadOnlyDictionary<int, SchemaHintsResultSet> resultSetHints,
        out ResultSetMetadata metadata)
    {
        resultSetHints.TryGetValue(schema.Index, out var hint);

        var delivery = NormalizeDelivery(schema.Delivery);
        var deliverySource = "RS0.delivery";
        if (delivery is null && hint?.Delivery is not null)
        {
            delivery = NormalizeDelivery(hint.Delivery);
            if (delivery is not null)
                deliverySource = "SchemaHintsJson.resultSets";
        }

        if (string.IsNullOrWhiteSpace(delivery))
        {
            metadata = null!;
            return false;
        }

        var tableName = schema.TableName;
        if ((string.IsNullOrWhiteSpace(tableName) || IsPlaceholderTableName(tableName, schema.Index)) &&
            !string.IsNullOrWhiteSpace(hint?.DatasetName))
        {
            tableName = hint.DatasetName;
        }

        if (string.IsNullOrWhiteSpace(tableName))
            tableName = $"rs{schema.Index}";

        var tableKind = !string.IsNullOrWhiteSpace(schema.TableKind) ? schema.TableKind : hint?.TableKind;

        metadata = new ResultSetMetadata(schema.Index, tableName!, tableKind, delivery, deliverySource);
        return true;
    }

    private static string? NormalizeDelivery(string? delivery)
    {
        if (string.IsNullOrWhiteSpace(delivery))
            return null;

        var normalized = delivery.Trim().ToLowerInvariant();
        return normalized switch
        {
            "engine" => normalized,
            "display" => normalized,
            "both" => normalized,
            _ => null
        };
    }

    private static bool IsPlaceholderTableName(string? tableName, int rsIndex)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return true;

        return string.Equals(tableName, $"rs{rsIndex}", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ResolvePrimaryKey(AtomicResultSetSchema schema, SchemaHintsTable? hints)
    {
        if (schema.PrimaryKey is { Count: > 0 })
            return schema.PrimaryKey;

        if (hints?.PrimaryKey is { Count: > 0 })
            return hints.PrimaryKey;

        return Array.Empty<string>();
    }

    private static IReadOnlyList<string> ResolveJoinHints(
        AtomicResultSetSchema schema,
        SchemaHintsTable? hints,
        List<string> warnings)
    {
        if (!string.IsNullOrWhiteSpace(schema.JoinHints))
            return ParseJoinHints(schema.JoinHints);

        if (hints?.ForeignKeys is { Count: > 0 })
        {
            return hints.ForeignKeys
                .Select(fk => fk.Column)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (hints?.PrimaryKey is { Count: > 0 })
            return hints.PrimaryKey;

        warnings.Add($"Join hints missing for table '{schema.TableName}'. Provide RS0 joinHints or SPCatalog.SchemaHintsJson.");
        return Array.Empty<string>();
    }

    private static IReadOnlyList<SchemaHintsForeignKey> ResolveForeignKeys(
        SchemaHintsTable? hints,
        IReadOnlyList<string> joinHints,
        IReadOnlyList<string> primaryKey)
    {
        if (hints?.ForeignKeys is { Count: > 0 })
            return hints.ForeignKeys;

        var pk = new HashSet<string>(primaryKey ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var list = joinHints.Where(h => !pk.Contains(h))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(h => new SchemaHintsForeignKey(h, null, null))
            .ToList();

        return list;
    }

    private static IReadOnlyList<string> ResolveMeasureHints(AtomicResultSetSchema schema, SchemaHintsTable? hints)
    {
        var fromSchema = ExtractColumnNamesByRole(schema, "measure");
        if (fromSchema.Count > 0)
            return fromSchema;

        return hints?.MeasureHints ?? Array.Empty<string>();
    }

    private static IReadOnlyList<string> ResolveDimensionHints(AtomicResultSetSchema schema, SchemaHintsTable? hints)
    {
        var fromSchema = ExtractColumnNamesByRole(schema, "dimension");
        if (fromSchema.Count > 0)
            return fromSchema;

        return hints?.DimensionHints ?? Array.Empty<string>();
    }

    private static IReadOnlyList<string> ExtractColumnNamesByRole(AtomicResultSetSchema schema, string role)
    {
        if (schema.Columns is null || schema.Columns.Count == 0)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var c in schema.Columns)
        {
            if (string.IsNullOrWhiteSpace(c.Name))
                continue;

            if (string.Equals(c.Role, role, StringComparison.OrdinalIgnoreCase))
                list.Add(c.Name);
        }

        return list;
    }

    private static object BuildEngineDatasetDigest(
        string datasetId,
        string tableName,
        IReadOnlyList<object> columns,
        IReadOnlyList<string> primaryKey,
        IReadOnlyList<string> joinHints,
        IReadOnlyList<string> measures,
        IReadOnlyList<string> dimensions)
    {
        return new
        {
            datasetId,
            tableName,
            columns,
            primaryKey,
            joinHints,
            measures,
            dimensions
        };
    }

    private static IReadOnlyList<string> ParseJoinHints(string joinHints)
    {
        var parts = joinHints.Split(new[] { ',', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in parts)
        {
            var token = raw.Trim();
            if (string.IsNullOrWhiteSpace(token))
                continue;

            if (token.Contains("=", StringComparison.Ordinal))
            {
                var sides = token.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                foreach (var side in sides)
                {
                    var name = ExtractColumnName(side);
                    if (seen.Add(name))
                        list.Add(name);
                }
                continue;
            }

            var colName = ExtractColumnName(token);
            if (seen.Add(colName))
                list.Add(colName);
        }

        return list;
    }

    private static string ExtractColumnName(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.Contains('.', StringComparison.Ordinal))
            trimmed = trimmed.Split('.').Last().Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? token.Trim() : trimmed;
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

    /// <summary>
    /// Build a generic filter payload from RS1 summary using deterministic schema.
    ///
    /// Rule:
    /// - Prefer schema columns where semanticType='filter' or role='filter'.
    /// - Fallback: column name ending with '*Filter'.
    ///
    /// This removes hard-coded filter keys (season/collection/range...) to support many modules.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> BuildFiltersFromSummary(AtomicQueryResult atomic)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var summary = atomic?.Summary;
        if (summary?.Table is null || summary.Table.Rows.Count == 0)
            return dict;

        var row = summary.Table.Rows[0];
        var semByName = new Dictionary<string, (string? role, string? semType)>(StringComparer.OrdinalIgnoreCase);

        if (summary.Schema?.Columns is not null)
        {
            foreach (var c in summary.Schema.Columns)
            {
                if (string.IsNullOrWhiteSpace(c.Name)) continue;
                semByName[c.Name] = (c.Role, c.SemanticType);
            }
        }

        for (var i = 0; i < summary.Table.Columns.Count; i++)
        {
            var name = summary.Table.Columns[i].Name;
            if (string.IsNullOrWhiteSpace(name)) continue;

            semByName.TryGetValue(name, out var sem);

            var isFilter = string.Equals(sem.semType, "filter", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(sem.role, "filter", StringComparison.OrdinalIgnoreCase)
                           || name.EndsWith("Filter", StringComparison.OrdinalIgnoreCase);

            if (!isFilter) continue;

            var v = row.Count() > i ? row[i] : null;
            if (v is null) continue;

            // Do not include empty strings.
            if (v is string s && string.IsNullOrWhiteSpace(s))
                continue;

            dict[name] = v;
        }

        return dict;
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

        private static (IReadOnlyDictionary<string, object?> Parameters, IReadOnlyList<string> NormalizedKeys) ParseSqlParameters(JsonElement? je)
    {
        if (je is null || je.Value.ValueKind == JsonValueKind.Undefined || je.Value.ValueKind == JsonValueKind.Null)
            return (new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase), Array.Empty<string>());

        if (je.Value.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("params must be a JSON object.");

        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>();

        foreach (var prop in je.Value.EnumerateObject())
        {
            var originalKey = prop.Name ?? string.Empty;
            var trimmed = originalKey.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var normalizedKey = trimmed.StartsWith("@", StringComparison.Ordinal) ? trimmed : "@" + trimmed;
            if (!string.Equals(originalKey, normalizedKey, StringComparison.Ordinal) || !string.Equals(originalKey, trimmed, StringComparison.Ordinal))
                normalized.Add($"{originalKey}->{normalizedKey}");

            dict[normalizedKey] = ConvertJsonValue(prop.Value);
        }

        return (dict, normalized);
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
            var copy = new Dictionary<string, object?>(parameters, StringComparer.OrdinalIgnoreCase);
            return (copy, new List<string>());
        }

        var filtered = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var dropped = new List<string>();

        foreach (var kv in parameters)
        {
            var normalized = AtomicCatalogService.NormalizeParamName(kv.Key);
            if (allowedParams.Contains(normalized))
            {
                // Keep normalized key; repository will normalize to @param.
                filtered[kv.Key] = kv.Value;
            }
            else
            {
                dropped.Add(kv.Key);
            }
        }

        return (filtered, dropped);
    }
private static IReadOnlyDictionary<string, object?> ApplyDefaults(
        IReadOnlyDictionary<string, object?> parameters,
        IReadOnlyDictionary<string, object?> defaults,
        IReadOnlySet<string> allowedParams)
    {
        var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (parameters is not null)
        {
            foreach (var kv in parameters)
            {
                var name = AtomicCatalogService.NormalizeParamName(kv.Key);
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                merged[name] = kv.Value;
            }
        }

        if (defaults is not null)
        {
            foreach (var kv in defaults)
            {
                if (string.IsNullOrWhiteSpace(kv.Key))
                    continue;

                if (allowedParams is not null && allowedParams.Count > 0 && !allowedParams.Contains(kv.Key))
                    continue;

                if (!merged.ContainsKey(kv.Key))
                    merged[kv.Key] = kv.Value;
            }
        }

        return merged;
    }

    private static string[] FormatExpectedParamsForHumans(IReadOnlySet<string> allowedParams)
    {
        if (allowedParams is null || allowedParams.Count == 0) return Array.Empty<string>();
        // Present without '@' for LLM friendliness, but still derived from ParamsJson (source of truth).
        return allowedParams
            .Select(p => p.StartsWith("@", StringComparison.Ordinal) ? p.Substring(1) : p)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, object?> BuildEffectiveParamsEcho(IReadOnlyDictionary<string, object?> parameters)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (parameters is null) return dict;

        foreach (var kv in parameters)
        {
            var k = kv.Key?.Trim();
            if (string.IsNullOrWhiteSpace(k)) continue;
            if (k.StartsWith("@", StringComparison.Ordinal)) k = k.Substring(1);

            object? v = kv.Value;
            if (v is string s)
            {
                s = s.Trim();
                if (s.Length > 200) s = s.Substring(0, 200) + "...";
                v = s;
            }
            dict[k] = v;
        }
        return dict;
    }

    private static object BuildStrictParamFailurePayload(
        string spName,
        AtomicCatalogEntry catalogEntry,
        string code,
        string message,
        IReadOnlyList<string> expectedParams,
        IReadOnlyList<string> receivedParams)
    {
        // Must conform to atomic.query.execute.v1 schema: keep required data fields and carry error details inside data.schema (open object).
        return new
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
                // Carry contract error info here (data.schema allows arbitrary object).
                schema = new
                {
                    error = new { code, message },
                    expectedParams,
                    receivedParams,
                    example = expectedParams.Count > 0
                        ? new { Season = "2023/2024,2024/2025", Page = 0, Size = 20000 }
                        : null
                },
                summary = (object?)null,
                displayTables = Array.Empty<object>(),
                engineDatasets = Array.Empty<object>()
            },
            warnings = new[]
            {
                $"{code}: {message}",
                expectedParams.Count > 0 ? $"Expected params (from ParamsJson): {string.Join(", ", expectedParams)}" : "Expected params (from ParamsJson): <missing>",
                receivedParams.Count > 0 ? $"Received params: {string.Join(", ", receivedParams)}" : "Received params: <none>",
                "Rule: When calling atomic.query.execute, ONLY use the parameter names returned by atomic.catalog.search -> results[].parameters[].name. Do not use output column names from RS1/RS2 (e.g., seasonFilter)."
            }
        };
    }

        private static object BuildSchemaMetadataFailurePayload(
        string spName,
        AtomicCatalogEntry catalogEntry,
        IReadOnlyList<object> missingResultSets,
        string message)
    {
        return new
        {
            kind = "atomic.query.execute.v1",
            schemaVersion = 2,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "atomic.query.execute",
            error = new
            {
                code = "SCHEMA_METADATA_REQUIRED",
                message
            },
            remediation = new
            {
                rs0 = "Ensure RS0 includes resultset rows with delivery (engine|display|both) and datasetName/tableName for each RS2..N data table.",
                schemaHints = "Alternatively set dbo.TILSOFTAI_SPCatalog.SchemaHintsJson.resultSets with rsIndex/datasetName/delivery/tableKind."
            },
            missingResultSets,
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
                schema = (object?)null,
                summary = (object?)null,
                displayTables = Array.Empty<object>(),
                engineDatasets = Array.Empty<object>()
            },
            schemaHintsJson = new
            {
                resultSets = new[]
                {
                    new { rsIndex = 2, datasetName = "sales_engine", delivery = "engine", tableKind = "fact" }
                }
            },
            warnings = new[]
            {
                $"SCHEMA_METADATA_REQUIRED: {message}"
            }
        };
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

    private static RoutingDecision ResolveDelivery(ResultSetMetadata meta)
    {
        return meta.Delivery switch
        {
            "engine" => new RoutingDecision(Engine: true, Display: false, Reason: $"{meta.DeliverySource}=engine"),
            "display" => new RoutingDecision(Engine: false, Display: true, Reason: $"{meta.DeliverySource}=display"),
            "both" => new RoutingDecision(Engine: true, Display: true, Reason: $"{meta.DeliverySource}=both"),
            _ => new RoutingDecision(Engine: false, Display: false, Reason: $"{meta.DeliverySource}=missing")
        };
    }

    private sealed record RoutingDecision(bool Engine, bool Display, string Reason);

    private sealed record RoutingPolicyOptions(int MaxDisplayRows)
    {
        public static RoutingPolicyOptions Default => new(MaxDisplayRows: 2000);
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













