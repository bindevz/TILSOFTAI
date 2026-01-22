using System.Text.Json;
using Microsoft.Extensions.Options;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Configuration;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Chat.Localization;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Formatting;
using TILSOFTAI.Orchestration.Execution;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Analytics.Handlers;

public sealed class AnalyticsRunToolHandler : IToolHandler
{
    public string ToolName => "analytics.run";

    private readonly AnalyticsService _analyticsService;
    private readonly ExecutionContextAccessor _ctxAccessor;
    private readonly AppSettings _settings;
    private readonly IChatTextLocalizer _localizer;

    public AnalyticsRunToolHandler(
        AnalyticsService analyticsService,
        ExecutionContextAccessor ctxAccessor,
        IOptions<AppSettings> settings,
        IChatTextLocalizer localizer)
    {
        _analyticsService = analyticsService;
        _ctxAccessor = ctxAccessor;
        _settings = settings.Value;
        _localizer = localizer;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var datasetId = dyn.GetStringRequired("datasetId");
        var pipeline = dyn.GetJsonRequired("pipeline");
        var persistResult = dyn.GetBool("persistResult", false);

        var topN = Math.Clamp(dyn.GetInt("topN", 20), 1, 200);
        var bounds = new AnalyticsService.RunBounds(
            TopN: topN,
            MaxGroups: _settings.AnalyticsEngine.MaxGroups,
            MaxResultRows: _settings.AnalyticsEngine.MaxResultRows,
            MaxJoinRows: _settings.AnalyticsEngine.MaxJoinRows,
            MaxJoinMatchesPerLeft: _settings.AnalyticsEngine.MaxJoinMatchesPerLeft);

        if (!_analyticsService.TryGetDatasetSchema(datasetId, context, out var baseSchema, out var schemaError))
        {
            var errorPayload = BuildFailurePayload(datasetId, "dataset_not_found", schemaError ?? "Dataset not found or expired.");
            return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateFailure("analytics.run failed", errorPayload));
        }

        var validationErrors = ValidatePipeline(pipeline, baseSchema, context);
        if (validationErrors.Count > 0)
        {
            var errorPayload = BuildFailurePayload(datasetId, "invalid_pipeline", _localizer.Get(ChatTextKeys.ErrorInvalidPlan), validationErrors);
            return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateFailure("analytics.run invalid pipeline", errorPayload));
        }

        AnalyticsService.RunResult result;
        try
        {
            result = await _analyticsService.RunAsync(datasetId, pipeline, bounds, context, cancellationToken, persistResult);
        }
        catch (Exception ex)
        {
            var errorPayload = BuildFailurePayload(datasetId, "execution_failed", ex.Message);
            return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateFailure("analytics.run failed", errorPayload));
        }

        var previewRowLimit = Math.Clamp(_settings.AnalyticsEngine.PreviewRowLimit, 1, 200);
        var previewRows = result.Rows.Take(previewRowLimit).ToArray();
        var payloadEvidence = new
        {
            summarySchema = new
            {
                columns = result.Schema.Select(c => new { name = c.Name, dataType = c.DataType, displayName = c.DisplayName })
            },
            previewRows = new
            {
                columns = result.Schema.Select(c => c.Name),
                rows = previewRows
            },
            resultDatasetId = result.ResultDatasetId
        };

        var payload = new
        {
            kind = "analytics.run.v1",
            schemaVersion = 2,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "analytics.run",
            ok = true,
            data = new
            {
                datasetId = result.DatasetId,
                rowCount = result.RowCount,
                columnCount = result.ColumnCount,
                warnings = result.Warnings,
                resultDatasetId = result.ResultDatasetId
            },
            evidence = payloadEvidence
        };

        var renderOptions = new MarkdownTableRenderOptions { MaxRows = previewRowLimit };
        _ctxAccessor.LastInsightPreviewMarkdown = MarkdownTableRenderer.Render(new AnalyticsSchema(result.Schema), previewRows, renderOptions);

        var evidence = new List<EnvelopeEvidenceItemV1>
        {
            new EnvelopeEvidenceItemV1
            {
                Id = "summary_schema",
                Type = "metric",
                Title = "Summary schema",
                Payload = new
                {
                    columns = result.Schema.Select(c => new { name = c.Name, dataType = c.DataType, displayName = c.DisplayName })
                }
            },
            new EnvelopeEvidenceItemV1
            {
                Id = "summary_rows_preview",
                Type = "list",
                Title = "Summary rows preview",
                Payload = new
                {
                    columns = result.Schema.Select(c => c.Name),
                    rows = previewRows
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(result.ResultDatasetId))
        {
            evidence.Add(new EnvelopeEvidenceItemV1
            {
                Id = "result_dataset",
                Type = "metric",
                Title = "Result dataset",
                Payload = new { resultDatasetId = result.ResultDatasetId }
            });
        }

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1 { System = "analytics", Name = "atomic-data-engine", Cache = "na" },
            Evidence: evidence);

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("analytics.run executed", payload), extras);
    }

    private IReadOnlyList<string> ValidatePipeline(JsonElement pipeline, IReadOnlyList<AnalyticsService.AnalyticsColumn> baseSchema, TSExecutionContext context)
    {
        var errors = new List<string>();
        var steps = TryEnumerateSteps(pipeline, out var parseError);
        if (steps is null)
        {
            errors.Add(parseError ?? "pipeline must be an array or { steps: [...] }.");
            return errors;
        }

        var columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in baseSchema)
        {
            if (!string.IsNullOrWhiteSpace(c.Name))
                columns[c.Name] = c.DataType;
        }

        var allowedOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "filter", "groupby", "sort", "topn", "select", "join", "derive", "percentoftotal", "datebucket"
        };

        foreach (var step in steps)
        {
            if (step.ValueKind != JsonValueKind.Object)
                continue;

            if (!step.TryGetProperty("op", out var opEl) || opEl.ValueKind != JsonValueKind.String)
            {
                errors.Add("pipeline step is missing 'op'.");
                continue;
            }

            var op = (opEl.GetString() ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(op))
            {
                errors.Add("pipeline step has empty 'op'.");
                continue;
            }

            var opNorm = op.Trim().ToLowerInvariant();
            if (!allowedOps.Contains(opNorm))
            {
                errors.Add($"Unsupported op '{op}'. Allowed: {string.Join(", ", allowedOps)}.");
                continue;
            }

            switch (opNorm)
            {
                case "filter":
                    ValidateFilter(step, columns, errors);
                    break;
                case "groupby":
                    columns = ValidateGroupBy(step, columns, errors);
                    break;
                case "sort":
                    ValidateSort(step, columns, errors);
                    break;
                case "topn":
                    break;
                case "select":
                    columns = ValidateSelect(step, columns, errors);
                    break;
                case "join":
                    columns = ValidateJoin(step, columns, context, errors);
                    break;
                case "derive":
                    columns = ValidateDerive(step, columns, errors);
                    break;
                case "percentoftotal":
                    columns = ValidatePercentOfTotal(step, columns, errors);
                    break;
                case "datebucket":
                    columns = ValidateDateBucket(step, columns, errors);
                    break;
            }
        }

        return errors;
    }

    private static IEnumerable<JsonElement>? TryEnumerateSteps(JsonElement pipeline, out string? error)
    {
        error = null;
        if (pipeline.ValueKind == JsonValueKind.Array)
            return pipeline.EnumerateArray();

        if (pipeline.ValueKind == JsonValueKind.Object &&
            pipeline.TryGetProperty("steps", out var stepsEl) &&
            stepsEl.ValueKind == JsonValueKind.Array)
        {
            return stepsEl.EnumerateArray();
        }

        error = "pipeline must be an array or { steps: [...] }.";
        return null;
    }

    private static void ValidateFilter(JsonElement step, Dictionary<string, string> columns, List<string> errors)
    {
        if (!step.TryGetProperty("column", out var colEl) || colEl.ValueKind != JsonValueKind.String)
        {
            errors.Add("filter.column is required.");
            return;
        }

        var col = colEl.GetString() ?? string.Empty;
        if (!columns.ContainsKey(col))
            errors.Add($"filter.column '{col}' does not exist.");

        var allowedOperators = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "eq", "ne", "gt", "gte", "lt", "lte", "in", "between", "contains", "startswith"
        };

        var op = step.TryGetProperty("operator", out var opEl) && opEl.ValueKind == JsonValueKind.String
            ? opEl.GetString()
            : "eq";
        var opNorm = (op ?? "eq").Trim().ToLowerInvariant();
        if (!allowedOperators.Contains(opNorm))
            errors.Add($"filter.operator '{op}' is invalid. Allowed: {string.Join(", ", allowedOperators)}.");
    }

    private static Dictionary<string, string> ValidateGroupBy(JsonElement step, Dictionary<string, string> columns, List<string> errors)
    {
        var by = new List<string>();
        if (step.TryGetProperty("by", out var byEl) && byEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in byEl.EnumerateArray())
            {
                if (b.ValueKind != JsonValueKind.String) continue;
                var name = b.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    by.Add(name.Trim());
            }
        }

        if (by.Count == 0)
        {
            errors.Add("groupBy.by must be a non-empty array.");
            return columns;
        }

        foreach (var k in by)
        {
            if (!columns.ContainsKey(k))
                errors.Add($"groupBy.by contains unknown column '{k}'.");
        }

        var aggs = new List<(string op, string? column, string asName)>();
        var allowedAggOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "count", "sum", "avg", "min", "max"
        };
        if (step.TryGetProperty("aggregates", out var agEl) && agEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in agEl.EnumerateArray())
            {
                if (a.ValueKind != JsonValueKind.Object) continue;
                var op = a.TryGetProperty("op", out var opEl) && opEl.ValueKind == JsonValueKind.String ? opEl.GetString() : null;
                var col = a.TryGetProperty("column", out var colEl) && colEl.ValueKind == JsonValueKind.String ? colEl.GetString() : null;
                var asName = a.TryGetProperty("as", out var asEl) && asEl.ValueKind == JsonValueKind.String ? asEl.GetString() : null;
                if (string.IsNullOrWhiteSpace(op)) continue;
                var opNorm = op!.Trim().ToLowerInvariant();
                if (!allowedAggOps.Contains(opNorm))
                {
                    errors.Add($"aggregate.op '{op}' is invalid. Allowed: {string.Join(", ", allowedAggOps)}.");
                    continue;
                }
                var name = string.IsNullOrWhiteSpace(asName)
                    ? (opNorm == "count" ? "count" : $"{opNorm}_{col}")
                    : asName!.Trim();
                aggs.Add((opNorm, string.IsNullOrWhiteSpace(col) ? null : col!.Trim(), name));
            }
        }

        if (aggs.Count == 0)
            aggs.Add(("count", null, "count"));

        foreach (var agg in aggs)
        {
            if (agg.op == "count")
                continue;

            if (string.IsNullOrWhiteSpace(agg.column))
            {
                errors.Add($"aggregate '{agg.op}' requires 'column'.");
                continue;
            }

            if (!columns.TryGetValue(agg.column!, out var typeName))
            {
                errors.Add($"aggregate column '{agg.column}' does not exist.");
                continue;
            }

            if ((agg.op == "sum" || agg.op == "avg") && !IsNumericType(typeName))
                errors.Add($"aggregate '{agg.op}' requires numeric column '{agg.column}'.");
        }

        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in by)
            if (columns.TryGetValue(k, out var t)) next[k] = t;

        foreach (var agg in aggs)
            next[agg.asName] = "Double";

        return next;
    }

    private static void ValidateSort(JsonElement step, Dictionary<string, string> columns, List<string> errors)
    {
        if (!step.TryGetProperty("by", out var byEl) || byEl.ValueKind != JsonValueKind.String)
        {
            errors.Add("sort.by is required.");
            return;
        }

        var by = byEl.GetString() ?? string.Empty;
        if (!columns.ContainsKey(by))
            errors.Add($"sort.by '{by}' does not exist.");
    }

    private static Dictionary<string, string> ValidateSelect(JsonElement step, Dictionary<string, string> columns, List<string> errors)
    {
        var selected = new List<string>();
        if (step.TryGetProperty("columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in colsEl.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.String) continue;
                var name = c.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                    selected.Add(name.Trim());
            }
        }

        if (selected.Count == 0)
        {
            errors.Add("select.columns must be a non-empty array.");
            return columns;
        }

        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in selected)
        {
            if (!columns.TryGetValue(name, out var typeName))
            {
                errors.Add($"select.columns contains unknown column '{name}'.");
                continue;
            }
            next[name] = typeName;
        }

        return next;
    }

    private Dictionary<string, string> ValidateJoin(JsonElement step, Dictionary<string, string> columns, TSExecutionContext context, List<string> errors)
    {
        if (!step.TryGetProperty("rightDatasetId", out var dsEl) || dsEl.ValueKind != JsonValueKind.String)
        {
            errors.Add("join.rightDatasetId is required.");
            return columns;
        }

        var rightDatasetId = dsEl.GetString() ?? string.Empty;
        if (!_analyticsService.TryGetDatasetSchema(rightDatasetId, context, out var rightSchema, out var schemaError))
        {
            errors.Add(schemaError ?? $"join.rightDatasetId '{rightDatasetId}' not found.");
            return columns;
        }

        var leftKeys = ReadStringArray(step, "leftKeys");
        var rightKeys = ReadStringArray(step, "rightKeys");
        if (leftKeys.Count == 0 || rightKeys.Count == 0 || leftKeys.Count != rightKeys.Count)
            errors.Add("join.leftKeys and join.rightKeys must be non-empty arrays of the same length.");

        foreach (var key in leftKeys)
        {
            if (!columns.ContainsKey(key))
                errors.Add($"join.leftKeys contains unknown column '{key}'.");
        }

        var rightColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in rightSchema)
            rightColumns[c.Name] = c.DataType;

        foreach (var key in rightKeys)
        {
            if (!rightColumns.ContainsKey(key))
                errors.Add($"join.rightKeys contains unknown column '{key}'.");
        }

        var how = step.TryGetProperty("how", out var howEl) && howEl.ValueKind == JsonValueKind.String ? howEl.GetString() : "inner";
        if (!string.Equals(how, "inner", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(how, "left", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"join.how '{how}' is invalid. Use 'inner' or 'left'.");
        }

        var rightPrefix = step.TryGetProperty("rightPrefix", out var prefixEl) && prefixEl.ValueKind == JsonValueKind.String
            ? prefixEl.GetString()
            : "r_";
        if (string.IsNullOrWhiteSpace(rightPrefix))
        {
            rightPrefix = "r_";
            errors.Add("join.rightPrefix is empty; default prefix 'r_' is required.");
        }

        var selectRight = ReadStringArray(step, "selectRight");
        if (selectRight.Count > 50)
            errors.Add("join.selectRight exceeds max of 50 columns.");

        if (selectRight.Count == 0 && rightColumns.Count > 50)
            errors.Add("join.selectRight is required when right dataset has more than 50 columns.");
        var rightToInclude = selectRight.Count > 0 ? selectRight : rightColumns.Keys.ToList();

        foreach (var col in rightToInclude)
        {
            if (!rightColumns.ContainsKey(col))
            {
                errors.Add($"join.selectRight contains unknown column '{col}'.");
                continue;
            }

            var prefixed = rightPrefix + col;
            if (columns.ContainsKey(prefixed))
            {
                errors.Add($"join output column '{prefixed}' already exists.");
                continue;
            }

            columns[prefixed] = rightColumns[col];
        }

        return columns;
    }

    private static Dictionary<string, string> ValidateDerive(JsonElement step, Dictionary<string, string> columns, List<string> errors)
    {
        var asName = step.TryGetProperty("as", out var asEl) && asEl.ValueKind == JsonValueKind.String ? asEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(asName))
        {
            errors.Add("derive.as is required.");
            return columns;
        }

        var op = step.TryGetProperty("operator", out var opEl) && opEl.ValueKind == JsonValueKind.String ? opEl.GetString() : null;
        var opNorm = (op ?? string.Empty).Trim().ToLowerInvariant();
        var allowedOps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "add", "sub", "mul", "div" };
        if (!allowedOps.Contains(opNorm))
            errors.Add($"derive.operator '{op}' is invalid. Allowed: {string.Join(", ", allowedOps)}.");

        ValidateDeriveOperand(step, "left", columns, errors);
        ValidateDeriveOperand(step, "right", columns, errors);

        var next = new Dictionary<string, string>(columns, StringComparer.OrdinalIgnoreCase);
        if (!next.ContainsKey(asName!))
            next[asName!] = "Double";
        else
            errors.Add($"derive.as '{asName}' already exists.");

        return next;
    }

    private static void ValidateDeriveOperand(JsonElement step, string name, Dictionary<string, string> columns, List<string> errors)
    {
        if (!step.TryGetProperty(name, out var el))
        {
            errors.Add($"derive.{name} is required.");
            return;
        }

        if (TryGetOperandColumn(el, out var columnName))
        {
            if (!columns.TryGetValue(columnName, out var typeName))
            {
                errors.Add($"derive.{name} column '{columnName}' does not exist.");
            }
            else if (!IsNumericType(typeName))
            {
                errors.Add($"derive.{name} column '{columnName}' must be numeric.");
            }
            return;
        }

        if (!IsNumericLiteral(el))
            errors.Add($"derive.{name} must be a column name or numeric literal.");
    }

    private static Dictionary<string, string> ValidatePercentOfTotal(JsonElement step, Dictionary<string, string> columns, List<string> errors)
    {
        if (!step.TryGetProperty("column", out var colEl) || colEl.ValueKind != JsonValueKind.String)
        {
            errors.Add("percentOfTotal.column is required.");
            return columns;
        }

        var col = colEl.GetString() ?? string.Empty;
        if (!columns.TryGetValue(col, out var typeName))
        {
            errors.Add($"percentOfTotal.column '{col}' does not exist.");
            return columns;
        }

        if (!IsNumericType(typeName))
            errors.Add($"percentOfTotal.column '{col}' must be numeric.");

        var asName = step.TryGetProperty("as", out var asEl) && asEl.ValueKind == JsonValueKind.String
            ? asEl.GetString()
            : $"{col}_pct";

        var next = new Dictionary<string, string>(columns, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(asName))
        {
            if (next.ContainsKey(asName))
                errors.Add($"percentOfTotal.as '{asName}' already exists.");
            else
                next[asName] = "Double";
        }

        return next;
    }

    private static Dictionary<string, string> ValidateDateBucket(JsonElement step, Dictionary<string, string> columns, List<string> errors)
    {
        if (!step.TryGetProperty("column", out var colEl) || colEl.ValueKind != JsonValueKind.String)
        {
            errors.Add("dateBucket.column is required.");
            return columns;
        }

        var col = colEl.GetString() ?? string.Empty;
        if (!columns.ContainsKey(col))
            errors.Add($"dateBucket.column '{col}' does not exist.");

        var unit = step.TryGetProperty("unit", out var unitEl) && unitEl.ValueKind == JsonValueKind.String
            ? unitEl.GetString()
            : "month";
        var unitNorm = (unit ?? string.Empty).Trim().ToLowerInvariant();
        var allowedUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "day", "week", "month", "quarter", "year" };
        if (!allowedUnits.Contains(unitNorm))
            errors.Add($"dateBucket.unit '{unit}' is invalid. Allowed: {string.Join(", ", allowedUnits)}.");

        var asName = step.TryGetProperty("as", out var asEl) && asEl.ValueKind == JsonValueKind.String
            ? asEl.GetString()
            : $"{col}_{unitNorm}";

        var next = new Dictionary<string, string>(columns, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(asName))
        {
            if (next.ContainsKey(asName))
                errors.Add($"dateBucket.as '{asName}' already exists.");
            else
                next[asName] = "DateTime";
        }

        return next;
    }

    private static bool TryGetOperandColumn(JsonElement operand, out string? columnName)
    {
        columnName = null;
        if (operand.ValueKind == JsonValueKind.String)
        {
            columnName = operand.GetString();
            return !string.IsNullOrWhiteSpace(columnName);
        }

        if (operand.ValueKind == JsonValueKind.Object &&
            operand.TryGetProperty("column", out var colEl) &&
            colEl.ValueKind == JsonValueKind.String)
        {
            columnName = colEl.GetString();
            return !string.IsNullOrWhiteSpace(columnName);
        }

        return false;
    }

    private static bool IsNumericLiteral(JsonElement operand)
    {
        if (operand.ValueKind == JsonValueKind.Number)
            return true;

        return operand.ValueKind == JsonValueKind.Object
               && operand.TryGetProperty("value", out var valueEl)
               && valueEl.ValueKind == JsonValueKind.Number;
    }

    private static List<string> ReadStringArray(JsonElement step, string propName)
    {
        var values = new List<string>();
        if (!step.TryGetProperty(propName, out var el) || el.ValueKind != JsonValueKind.Array)
            return values;

        foreach (var v in el.EnumerateArray())
        {
            if (v.ValueKind != JsonValueKind.String) continue;
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s))
                values.Add(s.Trim());
        }

        return values;
    }

    private static bool IsNumericType(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            return false;

        return typeName.Equals("Int32", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("Int64", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("Double", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("Decimal", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("Single", StringComparison.OrdinalIgnoreCase)
            || typeName.Equals("Float", StringComparison.OrdinalIgnoreCase);
    }

    private static object BuildFailurePayload(string datasetId, string code, string message, IReadOnlyList<string>? errors = null)
    {
        return new
        {
            kind = "analytics.run.v1",
            schemaVersion = 2,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "analytics.run",
            ok = false,
            data = new
            {
                datasetId,
                rowCount = 0,
                columnCount = 0,
                warnings = Array.Empty<string>(),
                error = new
                {
                    code,
                    message
                },
                details = errors is null || errors.Count == 0 ? null : errors.ToArray()
            },
            evidence = new
            {
                summarySchema = new { columns = Array.Empty<object>() },
                previewRows = new { columns = Array.Empty<string>(), rows = Array.Empty<object?[]>() }
            }
        };
    }
}
