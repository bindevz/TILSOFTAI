using Microsoft.Data.Analysis;
using System.Globalization;
using System.Text.Json;

namespace TILSOFTAI.Analytics;

/// <summary>
/// Pure in-memory analytics engine.
///
/// This component is intentionally infrastructure-agnostic:
/// - Input: a dataset already loaded in-memory (DataFrame).
/// - Input: a JSON pipeline (bounded DSL).
/// - Output: a transformed DataFrame + warnings.
///
/// It is designed to mimic the common "python-pandas before LLM" pattern:
/// the system performs the heavy data crunching in-process, then only returns
/// compact summaries to the orchestrator/LLM.
/// </summary>
public sealed class AtomicDataEngine
{
    public EngineResult Execute(DataFrame dataset, JsonElement pipeline, EngineBounds bounds, Func<string, DataFrame?> datasetResolver)
    {
        if (dataset is null) throw new ArgumentNullException(nameof(dataset));
        bounds = bounds.WithDefaults();

        var plan = AnalysisPipeline.Parse(pipeline);
        var warnings = new List<string>(plan.Warnings);
        var result = AnalysisExecutor.Execute(dataset, plan, bounds, datasetResolver, warnings);
        return new EngineResult(result, warnings);
    }

    public EngineResult Execute(DataFrame dataset, JsonElement pipeline, EngineBounds bounds)
        => Execute(dataset, pipeline, bounds, _ => null);

    public sealed record EngineBounds(int TopN, int MaxGroups, int MaxJoinRows, int MaxJoinMatchesPerLeft, int MaxColumns, int MaxResultRows)
    {
        public EngineBounds WithDefaults()
        {
            var topN = TopN <= 0 ? 20 : Math.Clamp(TopN, 1, 200);
            var maxGroups = MaxGroups <= 0 ? 200 : Math.Clamp(MaxGroups, 1, 5_000);
            var maxJoinRows = MaxJoinRows <= 0 ? 100_000 : Math.Clamp(MaxJoinRows, 1, 200_000);
            var maxJoinMatches = MaxJoinMatchesPerLeft <= 0 ? 50 : Math.Clamp(MaxJoinMatchesPerLeft, 1, 5_000);
            var maxColumns = MaxColumns <= 0 ? 200 : Math.Clamp(MaxColumns, 1, 500);
            var maxResultRows = MaxResultRows <= 0 ? 500 : Math.Clamp(MaxResultRows, 1, 5_000);
            return new EngineBounds(topN, maxGroups, maxJoinRows, maxJoinMatches, maxColumns, maxResultRows);
        }
    }

    public sealed record EngineResult(DataFrame Data, IReadOnlyList<string> Warnings);

    // ------------------------
    // Pipeline parsing + execution (bounded DSL)
    // ------------------------

    internal sealed record AnalysisPipeline(IReadOnlyList<Step> Steps, IReadOnlyList<string> Warnings)
    {
        public static AnalysisPipeline Parse(JsonElement pipeline)
        {
            var steps = new List<Step>();
            var warnings = new List<string>();
            var stepsElement = pipeline;
            if (pipeline.ValueKind == JsonValueKind.Object &&
                pipeline.TryGetProperty("steps", out var stepsProp) &&
                stepsProp.ValueKind == JsonValueKind.Array)
            {
                stepsElement = stepsProp;
            }

            if (stepsElement.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("pipeline must be a JSON array or { steps: [...] }.");

            foreach (var s in stepsElement.EnumerateArray())
            {
                if (s.ValueKind != JsonValueKind.Object)
                    continue;

                if (!s.TryGetProperty("op", out var opEl) || opEl.ValueKind != JsonValueKind.String)
                    continue;

                var op = (opEl.GetString() ?? string.Empty).Trim().ToLowerInvariant();
                switch (op)
                {
                    case "filter":
                        steps.Add(FilterStep.Parse(s));
                        break;
                    case "groupby":
                        steps.Add(GroupByStep.Parse(s));
                        break;
                    case "sort":
                        steps.Add(SortStep.Parse(s));
                        break;
                    case "topn":
                        steps.Add(TopNStep.Parse(s));
                        break;
                    case "select":
                        steps.Add(SelectStep.Parse(s));
                        break;
                    case "join":
                        steps.Add(JoinStep.Parse(s));
                        break;
                    case "derive":
                        steps.Add(DeriveStep.Parse(s));
                        break;
                    case "percentoftotal":
                        steps.Add(PercentOfTotalStep.Parse(s));
                        break;
                    case "datebucket":
                        steps.Add(DateBucketStep.Parse(s));
                        break;
                    default:
                        warnings.Add($"Unsupported op '{op}' was ignored.");
                        break;
                }
            }

            return new AnalysisPipeline(steps, warnings);
        }
    }

    internal abstract record Step(string Op);

    internal sealed record FilterStep(string Column, string Operator, string? Value) : Step("filter")
    {
        public static FilterStep Parse(JsonElement e)
        {
            var col = e.TryGetProperty("column", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var op = e.TryGetProperty("operator", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : "eq";
            string? val = null;
            if (e.TryGetProperty("value", out var v))
            {
                val = v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => v.GetRawText()
                };
            }

            if (string.IsNullOrWhiteSpace(col))
                throw new ArgumentException("filter.column is required.");
            return new FilterStep(col!.Trim(), (op ?? "eq").Trim().ToLowerInvariant(), val);
        }
    }

    internal sealed record AggregateSpec(string Op, string? Column, string As);

    internal sealed record GroupByStep(IReadOnlyList<string> By, IReadOnlyList<AggregateSpec> Aggregates) : Step("groupBy")
    {
        public static GroupByStep Parse(JsonElement e)
        {
            var by = new List<string>();
            if (e.TryGetProperty("by", out var byEl) && byEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var b in byEl.EnumerateArray())
                {
                    if (b.ValueKind != JsonValueKind.String) continue;
                    var s = b.GetString();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    by.Add(s.Trim());
                }
            }
            if (by.Count == 0)
                throw new ArgumentException("groupBy.by must be a non-empty array.");

            var aggs = new List<AggregateSpec>();
            if (e.TryGetProperty("aggregates", out var agEl) && agEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in agEl.EnumerateArray())
                {
                    if (a.ValueKind != JsonValueKind.Object) continue;
                    var op = a.TryGetProperty("op", out var opEl) && opEl.ValueKind == JsonValueKind.String ? opEl.GetString() : null;
                    var col = a.TryGetProperty("column", out var colEl) && colEl.ValueKind == JsonValueKind.String ? colEl.GetString() : null;
                    var @as = a.TryGetProperty("as", out var asEl) && asEl.ValueKind == JsonValueKind.String ? asEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(op)) continue;
                    var opNorm = op!.Trim().ToLowerInvariant();
                    var asName = string.IsNullOrWhiteSpace(@as)
                        ? (opNorm == "count" ? "count" : $"{opNorm}_{col}")
                        : @as!.Trim();
                    aggs.Add(new AggregateSpec(opNorm, string.IsNullOrWhiteSpace(col) ? null : col!.Trim(), asName));
                }
            }

            if (aggs.Count == 0)
                aggs.Add(new AggregateSpec("count", null, "count"));

            return new GroupByStep(by, aggs);
        }
    }

    internal sealed record SortStep(string By, string Dir) : Step("sort")
    {
        public static SortStep Parse(JsonElement e)
        {
            var by = e.TryGetProperty("by", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null;
            var dir = e.TryGetProperty("dir", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : "desc";
            if (string.IsNullOrWhiteSpace(by)) throw new ArgumentException("sort.by is required.");
            return new SortStep(by!.Trim(), (dir ?? "desc").Trim().ToLowerInvariant());
        }
    }

    internal sealed record TopNStep(int N) : Step("topN")
    {
        public static TopNStep Parse(JsonElement e)
        {
            var n = 20;
            if (e.TryGetProperty("n", out var nEl))
            {
                if (nEl.ValueKind == JsonValueKind.Number && nEl.TryGetInt32(out var i)) n = i;
                else if (nEl.ValueKind == JsonValueKind.String && int.TryParse(nEl.GetString(), out var j)) n = j;
            }
            return new TopNStep(Math.Clamp(n, 1, 2000));
        }
    }

    internal sealed record SelectStep(IReadOnlyList<string> Columns) : Step("select")
    {
        public static SelectStep Parse(JsonElement e)
        {
            var cols = new List<string>();
            if (e.TryGetProperty("columns", out var c) && c.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in c.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.String) continue;
                    var s = x.GetString();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    cols.Add(s.Trim());
                }
            }
            if (cols.Count == 0) throw new ArgumentException("select.columns must be a non-empty array.");
            return new SelectStep(cols);
        }
    }

    internal sealed record JoinStep(
        string RightDatasetId,
        IReadOnlyList<string> LeftKeys,
        IReadOnlyList<string> RightKeys,
        string How,
        string RightPrefix,
        IReadOnlyList<string>? SelectRight) : Step("join")
    {
        public static JoinStep Parse(JsonElement e)
        {
            var rightDatasetId = e.TryGetProperty("rightDatasetId", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(rightDatasetId))
                throw new ArgumentException("join.rightDatasetId is required.");

            var leftKeys = ParseStringArray(e, "leftKeys");
            var rightKeys = ParseStringArray(e, "rightKeys");
            if (leftKeys.Count == 0 || rightKeys.Count == 0 || leftKeys.Count != rightKeys.Count)
                throw new ArgumentException("join.leftKeys and join.rightKeys must be non-empty arrays of the same length.");

            var how = e.TryGetProperty("how", out var howEl) && howEl.ValueKind == JsonValueKind.String
                ? howEl.GetString()
                : "inner";
            how = (how ?? "inner").Trim().ToLowerInvariant();
            if (how is not ("inner" or "left"))
                throw new ArgumentException("join.how must be 'inner' or 'left'.");

            var rightPrefix = e.TryGetProperty("rightPrefix", out var rpEl) && rpEl.ValueKind == JsonValueKind.String
                ? rpEl.GetString()
                : "r_";
            if (string.IsNullOrWhiteSpace(rightPrefix))
                rightPrefix = "r_";

            var selectRight = ParseStringArray(e, "selectRight");
            return new JoinStep(rightDatasetId!.Trim(), leftKeys, rightKeys, how, rightPrefix!, selectRight.Count == 0 ? null : selectRight);
        }

        private static List<string> ParseStringArray(JsonElement e, string name)
        {
            var list = new List<string>();
            if (e.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var x in arr.EnumerateArray())
                {
                    if (x.ValueKind != JsonValueKind.String) continue;
                    var s = x.GetString();
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    list.Add(s.Trim());
                }
            }
            return list;
        }
    }

    internal sealed record Operand(string? Column, decimal? Value);

    internal sealed record DeriveStep(string As, string Operator, Operand Left, Operand Right) : Step("derive")
    {
        public static DeriveStep Parse(JsonElement e)
        {
            var asName = e.TryGetProperty("as", out var asEl) && asEl.ValueKind == JsonValueKind.String ? asEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(asName))
                throw new ArgumentException("derive.as is required.");

            var op = e.TryGetProperty("operator", out var opEl) && opEl.ValueKind == JsonValueKind.String
                ? opEl.GetString()
                : "add";
            op = (op ?? "add").Trim().ToLowerInvariant();
            if (op is not ("add" or "sub" or "mul" or "div"))
                throw new ArgumentException("derive.operator must be add|sub|mul|div.");

            var left = ParseOperand(e, "left");
            var right = ParseOperand(e, "right");
            if (left is null || right is null)
                throw new ArgumentException("derive.left and derive.right are required.");

            return new DeriveStep(asName!.Trim(), op, left, right);
        }

        private static Operand? ParseOperand(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var el))
                return null;

            if (el.ValueKind == JsonValueKind.String)
            {
                var col = el.GetString();
                if (string.IsNullOrWhiteSpace(col))
                    return null;
                return new Operand(col.Trim(), null);
            }

            if (el.ValueKind == JsonValueKind.Number)
            {
                if (el.TryGetDecimal(out var dec))
                    return new Operand(null, dec);
                if (el.TryGetDouble(out var dbl))
                    return new Operand(null, (decimal)dbl);
            }

            if (el.ValueKind == JsonValueKind.Object)
            {
                if (el.TryGetProperty("column", out var colEl) && colEl.ValueKind == JsonValueKind.String)
                {
                    var col = colEl.GetString();
                    if (!string.IsNullOrWhiteSpace(col))
                        return new Operand(col.Trim(), null);
                }

                if (el.TryGetProperty("value", out var valEl) && valEl.ValueKind == JsonValueKind.Number)
                {
                    if (valEl.TryGetDecimal(out var dec))
                        return new Operand(null, dec);
                    if (valEl.TryGetDouble(out var dbl))
                        return new Operand(null, (decimal)dbl);
                }
            }

            return null;
        }
    }

    internal sealed record PercentOfTotalStep(string Column, string As) : Step("percentOfTotal")
    {
        public static PercentOfTotalStep Parse(JsonElement e)
        {
            var col = e.TryGetProperty("column", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            if (string.IsNullOrWhiteSpace(col))
                throw new ArgumentException("percentOfTotal.column is required.");

            var asName = e.TryGetProperty("as", out var asEl) && asEl.ValueKind == JsonValueKind.String
                ? asEl.GetString()
                : $"{col}_pct";

            return new PercentOfTotalStep(col!.Trim(), (asName ?? $"{col}_pct").Trim());
        }
    }

    internal sealed record DateBucketStep(string Column, string Unit, string As) : Step("dateBucket")
    {
        public static DateBucketStep Parse(JsonElement e)
        {
            var col = e.TryGetProperty("column", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            if (string.IsNullOrWhiteSpace(col))
                throw new ArgumentException("dateBucket.column is required.");

            var unit = e.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : "month";
            unit = (unit ?? "month").Trim().ToLowerInvariant();
            if (unit is not ("day" or "week" or "month" or "quarter" or "year"))
                throw new ArgumentException("dateBucket.unit must be day|week|month|quarter|year.");

            var asName = e.TryGetProperty("as", out var asEl) && asEl.ValueKind == JsonValueKind.String
                ? asEl.GetString()
                : $"{col}_{unit}";

            return new DateBucketStep(col!.Trim(), unit, (asName ?? $"{col}_{unit}").Trim());
        }
    }

    internal static class AnalysisExecutor
    {
        public static DataFrame Execute(DataFrame df, AnalysisPipeline pipeline, EngineBounds bounds, Func<string, DataFrame?> datasetResolver, List<string> warnings)
        {
            var current = df;
            foreach (var step in pipeline.Steps)
            {
                switch (step)
                {
                    case FilterStep f:
                        current = ApplyFilter(current, f);
                        break;
                    case SelectStep s:
                        current = ApplySelectColumns(current, s);
                        break;
                    case GroupByStep g:
                        current = ApplyGroupBy(current, g, bounds.MaxGroups, warnings);
                        break;
                    case SortStep s:
                        current = ApplySort(current, s);
                        break;
                    case TopNStep t:
                        current = ApplyTopN(current, Math.Min(t.N, bounds.TopN));
                        break;
                    case JoinStep j:
                        current = ApplyJoin(current, j, bounds, datasetResolver, warnings);
                        break;
                    case DeriveStep d:
                        current = ApplyDerive(current, d);
                        break;
                    case PercentOfTotalStep p:
                        current = ApplyPercentOfTotal(current, p);
                        break;
                    case DateBucketStep b:
                        current = ApplyDateBucket(current, b);
                        break;
                }
            }

            // Enforce final TopN even if pipeline did not specify.
            var maxRows = Math.Min(bounds.TopN, bounds.MaxResultRows);
            if (current.Rows.Count > maxRows)
            {
                current = ApplyTopN(current, maxRows);
                warnings.Add($"Result rows truncated to {maxRows} (maxResultRows).");
            }

            if (current.Columns.Count > bounds.MaxColumns)
            {
                current = ApplyColumnLimit(current, bounds.MaxColumns);
                warnings.Add($"Result columns truncated to {bounds.MaxColumns} (maxColumns).");
            }

            return current;
        }

        private static DataFrame ApplyFilter(DataFrame df, FilterStep step)
        {
            if (!TryGetColumn(df, step.Column, out var col))
                return df;

            var kind = GetColumnKind(col);
            var useDecimal = kind == ColumnKind.Numeric && col is DecimalDataFrameColumn;
            var op = step.Operator;
            var raw = step.Value?.Trim();

            var filterString = raw ?? string.Empty;
            var hasFilterValue = !string.IsNullOrWhiteSpace(filterString);

            var hasNumericFilter = false;
            var filterDecimal = 0m;
            var filterDouble = 0d;
            if (kind == ColumnKind.Numeric && hasFilterValue)
                hasNumericFilter = useDecimal ? TryParseDecimal(filterString, out filterDecimal) : TryParseDouble(filterString, out filterDouble);

            var hasDateFilter = false;
            var filterDate = DateTime.MinValue;
            if (kind == ColumnKind.DateTime && hasFilterValue)
                hasDateFilter = TryParseDate(filterString, out filterDate);

            var hasBoolFilter = false;
            var filterBool = false;
            if (kind == ColumnKind.Boolean && hasFilterValue)
                hasBoolFilter = TryParseBool(filterString, out filterBool);

            HashSet<string>? stringSet = null;
            HashSet<double>? doubleSet = null;
            HashSet<decimal>? decimalSet = null;
            HashSet<DateTime>? dateSet = null;
            HashSet<bool>? boolSet = null;

            if (op == "in")
            {
                var tokens = ParseTokens(filterString);
                switch (kind)
                {
                    case ColumnKind.Numeric:
                        if (useDecimal)
                            decimalSet = ToDecimalSet(tokens);
                        else
                            doubleSet = ToDoubleSet(tokens);
                        break;
                    case ColumnKind.DateTime:
                        dateSet = ToDateSet(tokens);
                        break;
                    case ColumnKind.Boolean:
                        boolSet = ToBoolSet(tokens);
                        break;
                    default:
                        stringSet = new HashSet<string>(tokens, StringComparer.OrdinalIgnoreCase);
                        break;
                }
            }

            var hasBetween = false;
            var minDec = 0m;
            var maxDec = 0m;
            var minDbl = 0d;
            var maxDbl = 0d;
            var minDate = DateTime.MinValue;
            var maxDate = DateTime.MinValue;
            string? minText = null;
            string? maxText = null;
            bool minBool = false;
            bool maxBool = false;

            if (op == "between" && TryParseBetweenTokens(filterString, out var minToken, out var maxToken))
            {
                switch (kind)
                {
                    case ColumnKind.Numeric:
                        if (useDecimal && TryParseDecimal(minToken, out minDec) && TryParseDecimal(maxToken, out maxDec))
                            hasBetween = true;
                        else if (!useDecimal && TryParseDouble(minToken, out minDbl) && TryParseDouble(maxToken, out maxDbl))
                            hasBetween = true;
                        break;
                    case ColumnKind.DateTime:
                        if (TryParseDate(minToken, out minDate) && TryParseDate(maxToken, out maxDate))
                            hasBetween = true;
                        break;
                    case ColumnKind.Boolean:
                        if (TryParseBool(minToken, out minBool) && TryParseBool(maxToken, out maxBool))
                            hasBetween = true;
                        break;
                    default:
                        minText = minToken;
                        maxText = maxToken;
                        hasBetween = true;
                        break;
                }
            }

            var keep = new List<long>();
            for (long i = 0; i < df.Rows.Count; i++)
            {
                var cell = col[i];
                var pass = op switch
                {
                    "eq" => EvaluateEq(cell, kind, useDecimal, filterString, hasNumericFilter, filterDecimal, filterDouble, hasDateFilter, filterDate, hasBoolFilter, filterBool),
                    "ne" => !EvaluateEq(cell, kind, useDecimal, filterString, hasNumericFilter, filterDecimal, filterDouble, hasDateFilter, filterDate, hasBoolFilter, filterBool),
                    "gt" => EvaluateCompare(cell, kind, useDecimal, filterString, hasNumericFilter, filterDecimal, filterDouble, hasDateFilter, filterDate, hasBoolFilter, filterBool, direction: 1),
                    "gte" => EvaluateCompare(cell, kind, useDecimal, filterString, hasNumericFilter, filterDecimal, filterDouble, hasDateFilter, filterDate, hasBoolFilter, filterBool, direction: 1, allowEqual: true),
                    "lt" => EvaluateCompare(cell, kind, useDecimal, filterString, hasNumericFilter, filterDecimal, filterDouble, hasDateFilter, filterDate, hasBoolFilter, filterBool, direction: -1),
                    "lte" => EvaluateCompare(cell, kind, useDecimal, filterString, hasNumericFilter, filterDecimal, filterDouble, hasDateFilter, filterDate, hasBoolFilter, filterBool, direction: -1, allowEqual: true),
                    "in" => EvaluateIn(cell, kind, useDecimal, stringSet, doubleSet, decimalSet, dateSet, boolSet),
                    "between" => EvaluateBetween(cell, kind, useDecimal, hasBetween, minDec, maxDec, minDbl, maxDbl, minDate, maxDate, minText, maxText, minBool, maxBool),
                    "contains" => (cell?.ToString() ?? string.Empty).IndexOf(filterString, StringComparison.OrdinalIgnoreCase) >= 0,
                    "startswith" => (cell?.ToString() ?? string.Empty).StartsWith(filterString, StringComparison.OrdinalIgnoreCase),
                    _ => EvaluateEq(cell, kind, useDecimal, filterString, hasNumericFilter, filterDecimal, filterDouble, hasDateFilter, filterDate, hasBoolFilter, filterBool)
                };

                if (pass)
                    keep.Add(i);
            }

            return TakeRows(df, keep);
        }

        private static DataFrame ApplySelectColumns(DataFrame df, SelectStep step)
        {
            var cols = new List<DataFrameColumn>();
            foreach (var name in step.Columns.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (TryGetColumn(df, name, out var c))
                    cols.Add(c);
            }
            return cols.Count == 0 ? df : new DataFrame(cols);
        }

        private static DataFrame ApplyGroupBy(DataFrame df, GroupByStep step, int maxGroups, List<string> warnings)
        {
            // Build a column index map.
            var colIdx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < df.Columns.Count; i++) colIdx[df.Columns[i].Name] = i;

            foreach (var k in step.By)
                if (!colIdx.ContainsKey(k))
                    throw new ArgumentException($"groupBy.by contains unknown column '{k}'.");

            foreach (var a in step.Aggregates)
            {
                if (a.Op != "count" && string.IsNullOrWhiteSpace(a.Column))
                    throw new ArgumentException($"aggregate '{a.Op}' requires 'column'.");
                if (a.Op != "count" && !colIdx.ContainsKey(a.Column!))
                    throw new ArgumentException($"aggregate column '{a.Column}' does not exist.");
            }

            var aggUseDecimal = new bool[step.Aggregates.Count];
            for (var i = 0; i < step.Aggregates.Count; i++)
            {
                var agg = step.Aggregates[i];
                if (agg.Op == "count" || string.IsNullOrWhiteSpace(agg.Column))
                    continue;

                var col = df.Columns[colIdx[agg.Column]];
                if (col is DecimalDataFrameColumn)
                    aggUseDecimal[i] = true;
            }

            var groups = new Dictionary<string, AggState>(StringComparer.OrdinalIgnoreCase);
            var groupsTruncated = false;

            for (long i = 0; i < df.Rows.Count; i++)
            {
                var keyParts = new string[step.By.Count];
                for (var k = 0; k < step.By.Count; k++)
                {
                    var c = df.Columns[step.By[k]];
                    keyParts[k] = (c[i]?.ToString() ?? string.Empty);
                }

                var key = string.Join("\u001F", keyParts);
                if (!groups.TryGetValue(key, out var st))
                {
                    if (groups.Count >= maxGroups)
                    {
                        groupsTruncated = true;
                        continue; // drop beyond cap
                    }
                    st = new AggState(keyParts, step.Aggregates, aggUseDecimal);
                    groups[key] = st;
                }

                st.Accumulate(df, i);
            }

            if (groupsTruncated)
                warnings.Add($"Groups truncated to {maxGroups}.");

            // Materialize result DataFrame.
            var resultColumns = new List<DataFrameColumn>();
            foreach (var by in step.By)
                resultColumns.Add(new StringDataFrameColumn(by, groups.Count));
            for (var i = 0; i < step.Aggregates.Count; i++)
            {
                var agg = step.Aggregates[i];
                if (agg.Op == "count")
                {
                    resultColumns.Add(new DoubleDataFrameColumn(agg.As, groups.Count));
                }
                else if (aggUseDecimal[i])
                {
                    resultColumns.Add(new DecimalDataFrameColumn(agg.As, groups.Count));
                }
                else
                {
                    resultColumns.Add(new DoubleDataFrameColumn(agg.As, groups.Count));
                }
            }

            var outDf = new DataFrame(resultColumns);
            var row = 0;
            foreach (var g in groups.Values)
            {
                for (var k = 0; k < step.By.Count; k++)
                    ((StringDataFrameColumn)outDf.Columns[step.By[k]])[row] = g.KeyParts[k];

                for (var ai = 0; ai < step.Aggregates.Count; ai++)
                    outDf.Columns[step.Aggregates[ai].As][row] = g.GetValue(ai);

                row++;
            }

            return outDf;
        }

        private static DataFrame ApplyJoin(
            DataFrame left,
            JoinStep step,
            EngineBounds bounds,
            Func<string, DataFrame?> datasetResolver,
            List<string> warnings)
        {
            var right = datasetResolver(step.RightDatasetId);
            if (right is null)
                throw new ArgumentException($"Join rightDatasetId '{step.RightDatasetId}' not found.");

            if (!TryGetColumnIndexes(left, step.LeftKeys, out var leftKeyIndexes, out var leftMissing))
            {
                warnings.Add($"Join skipped: left key columns missing: {string.Join(", ", leftMissing)}.");
                return left;
            }

            if (!TryGetColumnIndexes(right, step.RightKeys, out var rightKeyIndexes, out var rightMissing))
            {
                warnings.Add($"Join skipped: right key columns missing: {string.Join(", ", rightMissing)}.");
                return left;
            }

            var rightColumns = ResolveRightColumns(right, step);

            if (left.Columns.Count + rightColumns.Count > bounds.MaxColumns)
            {
                var allowed = Math.Max(0, bounds.MaxColumns - left.Columns.Count);
                if (allowed < rightColumns.Count)
                {
                    rightColumns = rightColumns.Take(allowed).ToList();
                    warnings.Add($"Join columns truncated to {bounds.MaxColumns} (maxColumns).");
                }
            }

            var maxIndexRows = (int)Math.Min(right.Rows.Count, bounds.MaxJoinRows);
            var rightIndex = BuildRightIndex(right, rightKeyIndexes, maxIndexRows, bounds.MaxJoinMatchesPerLeft, warnings);

            var outRows = new List<object?[]>(capacity: Math.Min(bounds.MaxJoinRows, 1024));
            var truncatedMatches = false;
            var truncatedRows = false;

            for (long li = 0; li < left.Rows.Count; li++)
            {
                var key = BuildCompositeKey(left, leftKeyIndexes, li);
                if (rightIndex.TryGetValue(key, out var matches))
                {
                    var added = 0;
                    foreach (var ri in matches)
                    {
                        if (outRows.Count >= bounds.MaxJoinRows)
                        {
                            truncatedRows = true;
                            break;
                        }

                        if (added >= bounds.MaxJoinMatchesPerLeft)
                        {
                            truncatedMatches = true;
                            break;
                        }

                        outRows.Add(BuildJoinRow(left, right, li, ri, rightColumns));
                        added++;
                    }

                    if (truncatedRows) break;
                }
                else if (string.Equals(step.How, "left", StringComparison.OrdinalIgnoreCase))
                {
                    if (outRows.Count >= bounds.MaxJoinRows)
                    {
                        truncatedRows = true;
                        break;
                    }

                    outRows.Add(BuildLeftJoinRow(left, li, rightColumns.Count));
                }
            }

            if (truncatedMatches)
                warnings.Add($"Join matches truncated to {bounds.MaxJoinMatchesPerLeft} per left row.");
            if (truncatedRows)
                warnings.Add($"Join rows truncated to {bounds.MaxJoinRows} rows.");

            return BuildJoinedDataFrame(left, right, outRows, rightColumns, step.RightPrefix, warnings);
        }

        private static DataFrame ApplyDerive(DataFrame df, DeriveStep step)
        {
            var leftIndex = step.Left.Column is not null ? GetColumnIndex(df, step.Left.Column) : -1;
            var rightIndex = step.Right.Column is not null ? GetColumnIndex(df, step.Right.Column) : -1;

            if (step.Left.Column is not null && leftIndex < 0)
                throw new ArgumentException($"derive.left column '{step.Left.Column}' does not exist.");
            if (step.Right.Column is not null && rightIndex < 0)
                throw new ArgumentException($"derive.right column '{step.Right.Column}' does not exist.");

            var useDecimal = IsDecimalOperand(df, step.Left, leftIndex)
                             || IsDecimalOperand(df, step.Right, rightIndex)
                             || IsDecimalLiteral(step.Left)
                             || IsDecimalLiteral(step.Right);

            var rowCount = df.Rows.Count;
            DataFrameColumn result = useDecimal
                ? new DecimalDataFrameColumn(step.As, rowCount)
                : new DoubleDataFrameColumn(step.As, rowCount);

            for (long i = 0; i < rowCount; i++)
            {
                if (!TryGetOperandValue(df, step.Left, leftIndex, i, useDecimal, out var leftDec, out var leftDbl)
                    || !TryGetOperandValue(df, step.Right, rightIndex, i, useDecimal, out var rightDec, out var rightDbl))
                {
                    result[i] = null;
                    continue;
                }

                if (useDecimal)
                {
                    decimal? value = step.Operator switch
                    {
                        "add" => leftDec + rightDec,
                        "sub" => leftDec - rightDec,
                        "mul" => leftDec * rightDec,
                        "div" => rightDec == 0m ? null : leftDec / rightDec,
                        _ => null
                    };

                    result[i] = value;
                }
                else
                {
                    double? value = step.Operator switch
                    {
                        "add" => leftDbl + rightDbl,
                        "sub" => leftDbl - rightDbl,
                        "mul" => leftDbl * rightDbl,
                        "div" => Math.Abs(rightDbl) < double.Epsilon ? null : leftDbl / rightDbl,
                        _ => null
                    };

                    result[i] = value;
                }
            }

            var cols = new List<DataFrameColumn>(df.Columns.Count + 1);
            foreach (var c in df.Columns)
                cols.Add(c);
            cols.Add(result);
            return new DataFrame(cols);
        }

        private static DataFrame ApplyPercentOfTotal(DataFrame df, PercentOfTotalStep step)
        {
            if (!TryGetColumn(df, step.Column, out var col))
                return df;

            var useDecimal = col is DecimalDataFrameColumn;
            var rowCount = df.Rows.Count;
            decimal totalDec = 0m;
            double totalDbl = 0d;

            for (long i = 0; i < rowCount; i++)
            {
                var value = col[i];
                if (useDecimal)
                {
                    if (TryGetDecimalValue(value, out var dec))
                        totalDec += dec;
                }
                else
                {
                    if (TryGetDoubleValue(value, out var dbl))
                        totalDbl += dbl;
                }
            }

            DataFrameColumn result = useDecimal
                ? new DecimalDataFrameColumn(step.As, rowCount)
                : new DoubleDataFrameColumn(step.As, rowCount);

            for (long i = 0; i < rowCount; i++)
            {
                var value = col[i];
                if (useDecimal)
                {
                    if (totalDec == 0m || !TryGetDecimalValue(value, out var dec))
                    {
                        result[i] = null;
                        continue;
                    }

                    result[i] = (dec / totalDec) * 100m;
                }
                else
                {
                    if (Math.Abs(totalDbl) < double.Epsilon || !TryGetDoubleValue(value, out var dbl))
                    {
                        result[i] = null;
                        continue;
                    }

                    result[i] = (dbl / totalDbl) * 100d;
                }
            }

            var cols = new List<DataFrameColumn>(df.Columns.Count + 1);
            foreach (var c in df.Columns)
                cols.Add(c);
            cols.Add(result);
            return new DataFrame(cols);
        }

        private static DataFrame ApplyDateBucket(DataFrame df, DateBucketStep step)
        {
            if (!TryGetColumn(df, step.Column, out var col))
                return df;

            var rowCount = df.Rows.Count;
            var bucket = new DateTimeDataFrameColumn(step.As, rowCount);

            for (long i = 0; i < rowCount; i++)
            {
                if (TryGetDateValue(col[i], out var dt))
                    bucket[i] = BucketDate(dt, step.Unit);
                else
                    bucket[i] = null;
            }

            var cols = new List<DataFrameColumn>(df.Columns.Count + 1);
            foreach (var c in df.Columns)
                cols.Add(c);
            cols.Add(bucket);
            return new DataFrame(cols);
        }

        private static DateTime BucketDate(DateTime dt, string unit)
        {
            var utc = dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime();
            return unit switch
            {
                "day" => new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc),
                "week" => new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc)
                    .AddDays(-((7 + (int)utc.DayOfWeek - (int)DayOfWeek.Monday) % 7)),
                "month" => new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                "quarter" =>
                    new DateTime(utc.Year, ((utc.Month - 1) / 3) * 3 + 1, 1, 0, 0, 0, DateTimeKind.Utc),
                "year" => new DateTime(utc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                _ => new DateTime(utc.Year, utc.Month, utc.Day, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        private static bool TryGetOperandValue(
            DataFrame df,
            Operand operand,
            int columnIndex,
            long rowIndex,
            bool useDecimal,
            out decimal dec,
            out double dbl)
        {
            dec = 0m;
            dbl = 0d;

            if (operand.Column is not null)
            {
                if (columnIndex < 0)
                    return false;

                var value = df.Columns[columnIndex][rowIndex];
                if (useDecimal)
                {
                    if (TryGetDecimalValue(value, out dec))
                    {
                        dbl = (double)dec;
                        return true;
                    }
                }
                else
                {
                    if (TryGetDoubleValue(value, out dbl))
                    {
                        dec = (decimal)dbl;
                        return true;
                    }
                }

                return false;
            }

            if (operand.Value is not null)
            {
                dec = operand.Value.Value;
                dbl = (double)dec;
                return true;
            }

            return false;
        }

        private static bool IsDecimalOperand(DataFrame df, Operand operand, int columnIndex)
        {
            if (operand.Column is null || columnIndex < 0)
                return false;

            return df.Columns[columnIndex] is DecimalDataFrameColumn;
        }

        private static bool IsDecimalLiteral(Operand operand)
        {
            if (operand.Column is not null || operand.Value is null)
                return false;

            return operand.Value.Value % 1m != 0m;
        }

        private sealed record JoinColumn(int Index, string Name);

        private static List<JoinColumn> ResolveRightColumns(DataFrame right, JoinStep step)
        {
            var rightCols = step.SelectRight is { Count: > 0 }
                ? step.SelectRight
                : right.Columns.Select(c => c.Name).ToList();

            var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < right.Columns.Count; i++)
                indexes[right.Columns[i].Name] = i;

            var list = new List<JoinColumn>();
            foreach (var name in rightCols)
            {
                if (indexes.TryGetValue(name, out var idx))
                    list.Add(new JoinColumn(idx, name));
            }

            return list;
        }

        private static Dictionary<CompositeKey, List<long>> BuildRightIndex(
            DataFrame right,
            int[] keyIndexes,
            int maxIndexRows,
            int maxMatchesPerKey,
            List<string> warnings)
        {
            var index = new Dictionary<CompositeKey, List<long>>(CompositeKeyComparer.Instance);
            var cappedRows = Math.Max(0, Math.Min((long)maxIndexRows, right.Rows.Count));
            var truncatedIndexRows = right.Rows.Count > cappedRows;
            var truncatedMatches = false;

            for (long i = 0; i < cappedRows; i++)
            {
                var key = BuildCompositeKey(right, keyIndexes, i);
                if (!index.TryGetValue(key, out var list))
                {
                    list = new List<long>();
                    index[key] = list;
                }

                if (list.Count >= maxMatchesPerKey)
                {
                    truncatedMatches = true;
                    continue;
                }

                list.Add(i);
            }

            if (truncatedIndexRows)
                warnings.Add($"Join right index truncated to {cappedRows} rows.");
            if (truncatedMatches)
                warnings.Add($"Join right index matches capped at {maxMatchesPerKey} per key.");

            return index;
        }

        private static CompositeKey BuildCompositeKey(DataFrame df, int[] keyIndexes, long rowIndex)
        {
            var parts = new CompositeKeyPart[keyIndexes.Length];
            for (var i = 0; i < keyIndexes.Length; i++)
            {
                var v = df.Columns[keyIndexes[i]][rowIndex];
                parts[i] = CompositeKeyPart.From(v);
            }

            return new CompositeKey(parts);
        }

        private enum CompositeKeyPartKind
        {
            Null = 0,
            NumericDecimal = 1,
            NumericDouble = 2,
            DateTime = 3,
            Boolean = 4,
            String = 5
        }

        private readonly struct CompositeKeyPart : IEquatable<CompositeKeyPart>
        {
            public CompositeKeyPartKind Kind { get; }
            public decimal DecimalValue { get; }
            public double DoubleValue { get; }
            public long LongValue { get; }
            public bool BoolValue { get; }
            public string? TextValue { get; }

            private CompositeKeyPart(CompositeKeyPartKind kind, decimal dec, double dbl, long lng, bool bl, string? text)
            {
                Kind = kind;
                DecimalValue = dec;
                DoubleValue = dbl;
                LongValue = lng;
                BoolValue = bl;
                TextValue = text;
            }

            public static CompositeKeyPart From(object? value)
            {
                if (value is null)
                    return new CompositeKeyPart(CompositeKeyPartKind.Null, 0m, 0d, 0L, false, null);

                if (value is string s)
                {
                    if (TryParseNumericString(s, out var numericPart))
                        return numericPart;

                    return new CompositeKeyPart(CompositeKeyPartKind.String, 0m, 0d, 0L, false, s);
                }

                if (value is bool b)
                    return new CompositeKeyPart(CompositeKeyPartKind.Boolean, 0m, 0d, 0L, b, null);

                if (value is DateTime dt)
                    return new CompositeKeyPart(CompositeKeyPartKind.DateTime, 0m, 0d, NormalizeDateTicks(dt), false, null);

                if (value is DateTimeOffset dto)
                    return new CompositeKeyPart(CompositeKeyPartKind.DateTime, 0m, 0d, dto.UtcTicks, false, null);

                if (TryNormalizeNumeric(value, out var numeric))
                    return numeric;

                if (value is IFormattable formattable)
                {
                    var text = formattable.ToString(null, CultureInfo.InvariantCulture);
                    return new CompositeKeyPart(CompositeKeyPartKind.String, 0m, 0d, 0L, false, text);
                }

                return new CompositeKeyPart(CompositeKeyPartKind.String, 0m, 0d, 0L, false, value.ToString());
            }

            public bool Equals(CompositeKeyPart other)
            {
                if (Kind != other.Kind)
                    return false;

                return Kind switch
                {
                    CompositeKeyPartKind.Null => true,
                    CompositeKeyPartKind.NumericDecimal => DecimalValue == other.DecimalValue,
                    CompositeKeyPartKind.NumericDouble => DoubleValue.Equals(other.DoubleValue),
                    CompositeKeyPartKind.DateTime => LongValue == other.LongValue,
                    CompositeKeyPartKind.Boolean => BoolValue == other.BoolValue,
                    CompositeKeyPartKind.String => StringComparer.OrdinalIgnoreCase.Equals(TextValue ?? string.Empty, other.TextValue ?? string.Empty),
                    _ => false
                };
            }

            public override bool Equals(object? obj)
                => obj is CompositeKeyPart other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                hash.Add((int)Kind);
                switch (Kind)
                {
                    case CompositeKeyPartKind.NumericDecimal:
                        hash.Add(DecimalValue);
                        break;
                    case CompositeKeyPartKind.NumericDouble:
                        hash.Add(DoubleValue);
                        break;
                    case CompositeKeyPartKind.DateTime:
                        hash.Add(LongValue);
                        break;
                    case CompositeKeyPartKind.Boolean:
                        hash.Add(BoolValue);
                        break;
                    case CompositeKeyPartKind.String:
                        hash.Add(TextValue ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                        break;
                }
                return hash.ToHashCode();
            }

            private static bool TryNormalizeNumeric(object value, out CompositeKeyPart part)
            {
                switch (value)
                {
                    case decimal dec:
                        part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, dec, 0d, 0L, false, null);
                        return true;
                    case sbyte sb:
                        part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, sb, 0d, 0L, false, null);
                        return true;
                    case byte b:
                        part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, b, 0d, 0L, false, null);
                        return true;
                    case short s:
                        part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, s, 0d, 0L, false, null);
                        return true;
                    case ushort us:
                        part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, us, 0d, 0L, false, null);
                        return true;
                    case int i:
                        part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, i, 0d, 0L, false, null);
                        return true;
                    case uint ui:
                        part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, ui, 0d, 0L, false, null);
                        return true;
                    case long l:
                        part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, l, 0d, 0L, false, null);
                        return true;
                    case ulong ul:
                        part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, ul, 0d, 0L, false, null);
                        return true;
                    case float f:
                        return TryNormalizeDouble(f, out part);
                    case double d:
                        return TryNormalizeDouble(d, out part);
                }

                part = default;
                return false;
            }

            private static bool TryNormalizeDouble(double value, out CompositeKeyPart part)
            {
                if (double.IsNaN(value) || double.IsInfinity(value))
                {
                    part = new CompositeKeyPart(CompositeKeyPartKind.NumericDouble, 0m, value, 0L, false, null);
                    return true;
                }

                try
                {
                    var dec = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, dec, 0d, 0L, false, null);
                    return true;
                }
                catch
                {
                    part = new CompositeKeyPart(CompositeKeyPartKind.NumericDouble, 0m, value, 0L, false, null);
                    return true;
                }
            }

            private static bool TryParseNumericString(string input, out CompositeKeyPart part)
            {
                if (decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var dec))
                {
                    part = new CompositeKeyPart(CompositeKeyPartKind.NumericDecimal, dec, 0d, 0L, false, null);
                    return true;
                }

                if (double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out var dbl))
                {
                    part = new CompositeKeyPart(CompositeKeyPartKind.NumericDouble, 0m, dbl, 0L, false, null);
                    return true;
                }

                part = default;
                return false;
            }

            private static long NormalizeDateTicks(DateTime value)
            {
                if (value.Kind == DateTimeKind.Unspecified)
                    value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
                else
                    value = value.ToUniversalTime();

                return value.Ticks;
            }
        }

        private readonly struct CompositeKey : IEquatable<CompositeKey>
        {
            private readonly CompositeKeyPart[] _parts;

            public CompositeKey(CompositeKeyPart[] parts)
            {
                _parts = parts;
            }

            public bool Equals(CompositeKey other)
            {
                if (_parts.Length != other._parts.Length)
                    return false;

                for (var i = 0; i < _parts.Length; i++)
                {
                    if (!_parts[i].Equals(other._parts[i]))
                        return false;
                }

                return true;
            }

            public override bool Equals(object? obj)
                => obj is CompositeKey other && Equals(other);

            public override int GetHashCode()
            {
                var hash = new HashCode();
                foreach (var part in _parts)
                    hash.Add(part);
                return hash.ToHashCode();
            }
        }

        private sealed class CompositeKeyComparer : IEqualityComparer<CompositeKey>
        {
            public static readonly CompositeKeyComparer Instance = new();

            public bool Equals(CompositeKey x, CompositeKey y) => x.Equals(y);

            public int GetHashCode(CompositeKey obj) => obj.GetHashCode();
        }

        private static object?[] BuildJoinRow(DataFrame left, DataFrame right, long leftRow, long rightRow, IReadOnlyList<JoinColumn> rightColumns)
        {
            var row = new object?[left.Columns.Count + rightColumns.Count];
            for (var i = 0; i < left.Columns.Count; i++)
                row[i] = left.Columns[i][leftRow];

            for (var i = 0; i < rightColumns.Count; i++)
                row[left.Columns.Count + i] = right.Columns[rightColumns[i].Index][rightRow];

            return row;
        }

        private static object?[] BuildLeftJoinRow(DataFrame left, long leftRow, int rightColumnCount)
        {
            var row = new object?[left.Columns.Count + rightColumnCount];
            for (var i = 0; i < left.Columns.Count; i++)
                row[i] = left.Columns[i][leftRow];
            return row;
        }

        private static DataFrame BuildJoinedDataFrame(
            DataFrame left,
            DataFrame right,
            IReadOnlyList<object?[]> rows,
            IReadOnlyList<JoinColumn> rightColumns,
            string rightPrefix,
            List<string> warnings)
        {
            var rowCount = rows.Count;
            var cols = new List<DataFrameColumn>(left.Columns.Count + rightColumns.Count);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var c in left.Columns)
            {
                usedNames.Add(c.Name);
                cols.Add(CreateEmptyColumnLike(c, rowCount, c.Name));
            }

            var renamed = false;
            foreach (var c in rightColumns)
            {
                var name = rightPrefix + c.Name;
                var unique = MakeUniqueName(usedNames, name);
                if (!string.Equals(unique, name, StringComparison.OrdinalIgnoreCase))
                    renamed = true;

                cols.Add(CreateEmptyColumnLike(right.Columns[c.Index], rowCount, unique));
            }

            if (renamed)
                warnings.Add("Join column name collisions detected; some right-side columns were renamed.");

            var outDf = new DataFrame(cols);
            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                for (var c = 0; c < cols.Count; c++)
                {
                    outDf.Columns[c][r] = row[c];
                }
            }

            return outDf;
        }

        private static string MakeUniqueName(HashSet<string> usedNames, string baseName)
        {
            if (usedNames.Add(baseName))
                return baseName;

            var i = 2;
            while (true)
            {
                var candidate = $"{baseName}_{i}";
                if (usedNames.Add(candidate))
                    return candidate;
                i++;
            }
        }

        private static bool TryGetColumnIndexes(
            DataFrame df,
            IReadOnlyList<string> names,
            out int[] indexes,
            out IReadOnlyList<string> missing)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < df.Columns.Count; i++)
                map[df.Columns[i].Name] = i;

            var idx = new int[names.Count];
            var missingList = new List<string>();
            for (var i = 0; i < names.Count; i++)
            {
                var name = names[i];
                if (!map.TryGetValue(name, out var index))
                {
                    missingList.Add(name);
                    continue;
                }
                idx[i] = index;
            }

            indexes = idx;
            missing = missingList;
            return missingList.Count == 0;
        }

        private static DataFrame ApplyColumnLimit(DataFrame df, int maxColumns)
        {
            maxColumns = Math.Clamp(maxColumns, 1, 500);
            if (df.Columns.Count <= maxColumns)
                return df;

            var cols = new List<DataFrameColumn>(maxColumns);
            for (var i = 0; i < maxColumns; i++)
                cols.Add(df.Columns[i]);

            return new DataFrame(cols);
        }

        private static DataFrame ApplySort(DataFrame df, SortStep step)
        {
            var idx = GetColumnIndex(df, step.By);
            if (idx < 0) return df;

            var col = df.Columns[idx];
            var kind = GetColumnKind(col);
            var useDecimal = kind == ColumnKind.Numeric && col is DecimalDataFrameColumn;

            var order = Enumerable.Range(0, (int)df.Rows.Count)
                .Select(i => (long)i)
                .ToList();

            order.Sort((a, b) => CompareCells(col[a], col[b], kind, useDecimal));

            if (step.Dir == "desc")
                order.Reverse();

            return TakeRows(df, order);
        }

        private static DataFrame ApplyTopN(DataFrame df, int n)
        {
            n = Math.Clamp(n, 1, 5000);
            var take = (int)Math.Min(df.Rows.Count, n);
            var idx = Enumerable.Range(0, take).Select(i => (long)i).ToList();
            return TakeRows(df, idx);
        }

        private static DataFrame TakeRows(DataFrame df, IReadOnlyList<long> rowIndexes)
        {
            var rowCount = (long)rowIndexes.Count;

            var cols = new List<DataFrameColumn>(df.Columns.Count);
            foreach (var c in df.Columns)
                cols.Add(CreateEmptyColumnLike(c, rowCount));

            var outDf = new DataFrame(cols);

            for (var r = 0; r < rowIndexes.Count; r++)
            {
                var srcRow = rowIndexes[r];
                for (var colIndex = 0; colIndex < df.Columns.Count; colIndex++)
                {
                    // DataFrameColumn indexer is object-based, so this stays generic and avoids
                    // relying on concrete column types having Append().
                    outDf.Columns[colIndex][r] = df.Columns[colIndex][srcRow];
                }
            }

            return outDf;
        }

        private static DataFrameColumn CreateEmptyColumnLike(DataFrameColumn c, long length, string? nameOverride = null)
        {
            var name = nameOverride ?? c.Name;
            return c switch
            {
                Int32DataFrameColumn => new Int32DataFrameColumn(name, length),
                Int64DataFrameColumn => new Int64DataFrameColumn(name, length),
                DoubleDataFrameColumn => new DoubleDataFrameColumn(name, length),
                SingleDataFrameColumn => new SingleDataFrameColumn(name, length),
                DecimalDataFrameColumn => new DecimalDataFrameColumn(name, length),
                BooleanDataFrameColumn => new BooleanDataFrameColumn(name, length),
                DateTimeDataFrameColumn => new DateTimeDataFrameColumn(name, length),
                StringDataFrameColumn => new StringDataFrameColumn(name, length),
                _ => CreateFallbackColumn(c, name, length)
            };
        }

        private static DataFrameColumn CreateFallbackColumn(DataFrameColumn c, string name, long length)
        {
            if (c.DataType == typeof(DateTime))
                return new DateTimeDataFrameColumn(name, length);

            var columnType = c.GetType();
            var ctor = columnType.GetConstructor(new[] { typeof(string), typeof(long) });
            if (ctor is not null)
                return (DataFrameColumn)ctor.Invoke(new object?[] { name, length });

            if (length <= int.MaxValue)
            {
                ctor = columnType.GetConstructor(new[] { typeof(string), typeof(int) });
                if (ctor is not null)
                    return (DataFrameColumn)ctor.Invoke(new object?[] { name, (int)length });
            }

            return new StringDataFrameColumn(name, length);
        }

        private static bool TryGetColumn(DataFrame df, string name, out DataFrameColumn col)
        {
            col = null!;
            if (string.IsNullOrWhiteSpace(name)) return false;

            for (var i = 0; i < df.Columns.Count; i++)
            {
                var c = df.Columns[i];
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    col = c;
                    return true;
                }
            }

            return false;
        }

        private static int GetColumnIndex(DataFrame df, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return -1;
            for (var i = 0; i < df.Columns.Count; i++)
            {
                if (string.Equals(df.Columns[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private enum ColumnKind
        {
            Numeric,
            DateTime,
            Boolean,
            String
        }

        private static ColumnKind GetColumnKind(DataFrameColumn col)
        {
            var t = col.DataType;
            if (t == typeof(byte) || t == typeof(short) || t == typeof(int) || t == typeof(long)
                || t == typeof(float) || t == typeof(double) || t == typeof(decimal))
                return ColumnKind.Numeric;
            if (t == typeof(DateTime) || t == typeof(DateTimeOffset))
                return ColumnKind.DateTime;
            if (t == typeof(bool))
                return ColumnKind.Boolean;
            return ColumnKind.String;
        }

        private static bool EvaluateEq(
            object? cell,
            ColumnKind kind,
            bool useDecimal,
            string filterString,
            bool hasNumericFilter,
            decimal filterDecimal,
            double filterDouble,
            bool hasDateFilter,
            DateTime filterDate,
            bool hasBoolFilter,
            bool filterBool)
        {
            if (cell is null)
                return false;

            switch (kind)
            {
                case ColumnKind.Numeric:
                    if (!hasNumericFilter) return false;
                    if (useDecimal && TryGetDecimalValue(cell, out var dec))
                        return dec == filterDecimal;
                    if (!useDecimal && TryGetDoubleValue(cell, out var dbl))
                        return dbl == filterDouble;
                    return false;
                case ColumnKind.DateTime:
                    if (!hasDateFilter) return false;
                    if (TryGetDateValue(cell, out var dt))
                        return dt == filterDate;
                    return false;
                case ColumnKind.Boolean:
                    if (!hasBoolFilter) return false;
                    if (TryGetBoolValue(cell, out var b))
                        return b == filterBool;
                    return false;
                default:
                    return string.Equals(cell.ToString() ?? string.Empty, filterString, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool EvaluateCompare(
            object? cell,
            ColumnKind kind,
            bool useDecimal,
            string filterString,
            bool hasNumericFilter,
            decimal filterDecimal,
            double filterDouble,
            bool hasDateFilter,
            DateTime filterDate,
            bool hasBoolFilter,
            bool filterBool,
            int direction,
            bool allowEqual = false)
        {
            if (cell is null)
                return false;

            int cmp;
            switch (kind)
            {
                case ColumnKind.Numeric:
                    if (!hasNumericFilter) return false;
                    if (useDecimal && TryGetDecimalValue(cell, out var dec))
                        cmp = dec.CompareTo(filterDecimal);
                    else if (!useDecimal && TryGetDoubleValue(cell, out var dbl))
                        cmp = dbl.CompareTo(filterDouble);
                    else
                        return false;
                    break;
                case ColumnKind.DateTime:
                    if (!hasDateFilter) return false;
                    if (TryGetDateValue(cell, out var dt))
                        cmp = dt.CompareTo(filterDate);
                    else
                        return false;
                    break;
                case ColumnKind.Boolean:
                    if (!hasBoolFilter) return false;
                    if (TryGetBoolValue(cell, out var b))
                        cmp = b.CompareTo(filterBool);
                    else
                        return false;
                    break;
                default:
                    cmp = StringComparer.OrdinalIgnoreCase.Compare(cell.ToString() ?? string.Empty, filterString);
                    break;
            }

            return direction > 0
                ? allowEqual ? cmp >= 0 : cmp > 0
                : allowEqual ? cmp <= 0 : cmp < 0;
        }

        private static bool EvaluateIn(
            object? cell,
            ColumnKind kind,
            bool useDecimal,
            HashSet<string>? stringSet,
            HashSet<double>? doubleSet,
            HashSet<decimal>? decimalSet,
            HashSet<DateTime>? dateSet,
            HashSet<bool>? boolSet)
        {
            if (cell is null)
                return false;

            return kind switch
            {
                ColumnKind.Numeric when useDecimal && decimalSet is not null && TryGetDecimalValue(cell, out var dec)
                    => decimalSet.Contains(dec),
                ColumnKind.Numeric when !useDecimal && doubleSet is not null && TryGetDoubleValue(cell, out var dbl)
                    => doubleSet.Contains(dbl),
                ColumnKind.DateTime when dateSet is not null && TryGetDateValue(cell, out var dt)
                    => dateSet.Contains(dt),
                ColumnKind.Boolean when boolSet is not null && TryGetBoolValue(cell, out var b)
                    => boolSet.Contains(b),
                _ => stringSet is not null && stringSet.Contains(cell.ToString() ?? string.Empty)
            };
        }

        private static bool EvaluateBetween(
            object? cell,
            ColumnKind kind,
            bool useDecimal,
            bool hasBetween,
            decimal minDec,
            decimal maxDec,
            double minDbl,
            double maxDbl,
            DateTime minDate,
            DateTime maxDate,
            string? minText,
            string? maxText,
            bool minBool,
            bool maxBool)
        {
            if (!hasBetween || cell is null)
                return false;

            switch (kind)
            {
                case ColumnKind.Numeric:
                    if (useDecimal && TryGetDecimalValue(cell, out var dec))
                        return dec >= Math.Min(minDec, maxDec) && dec <= Math.Max(minDec, maxDec);
                    if (!useDecimal && TryGetDoubleValue(cell, out var dbl))
                        return dbl >= Math.Min(minDbl, maxDbl) && dbl <= Math.Max(minDbl, maxDbl);
                    return false;
                case ColumnKind.DateTime:
                    if (TryGetDateValue(cell, out var dt))
                    {
                        var min = minDate <= maxDate ? minDate : maxDate;
                        var max = minDate <= maxDate ? maxDate : minDate;
                        return dt >= min && dt <= max;
                    }
                    return false;
                case ColumnKind.Boolean:
                    if (TryGetBoolValue(cell, out var b))
                    {
                        var min = minBool ? 1 : 0;
                        var max = maxBool ? 1 : 0;
                        var val = b ? 1 : 0;
                        return val >= Math.Min(min, max) && val <= Math.Max(min, max);
                    }
                    return false;
                default:
                    var text = cell.ToString() ?? string.Empty;
                    var low = minText ?? string.Empty;
                    var high = maxText ?? string.Empty;
                    var cmpLow = StringComparer.OrdinalIgnoreCase.Compare(text, low);
                    var cmpHigh = StringComparer.OrdinalIgnoreCase.Compare(text, high);
                    if (StringComparer.OrdinalIgnoreCase.Compare(low, high) > 0)
                        return cmpLow <= 0 && cmpHigh >= 0;
                    return cmpLow >= 0 && cmpHigh <= 0;
            }
        }

        private static int CompareCells(object? a, object? b, ColumnKind kind, bool useDecimal)
        {
            if (a is null && b is null) return 0;
            if (a is null) return 1;
            if (b is null) return -1;

            switch (kind)
            {
                case ColumnKind.Numeric:
                    if (useDecimal && TryGetDecimalValue(a, out var da) && TryGetDecimalValue(b, out var db))
                        return da.CompareTo(db);
                    if (!useDecimal && TryGetDoubleValue(a, out var ad) && TryGetDoubleValue(b, out var bd))
                        return ad.CompareTo(bd);
                    break;
                case ColumnKind.DateTime:
                    if (TryGetDateValue(a, out var adt) && TryGetDateValue(b, out var bdt))
                        return adt.CompareTo(bdt);
                    break;
                case ColumnKind.Boolean:
                    if (TryGetBoolValue(a, out var ab) && TryGetBoolValue(b, out var bb))
                        return ab.CompareTo(bb);
                    break;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(a.ToString(), b.ToString());
        }

        private static List<string> ParseTokens(string? raw)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(raw))
                return tokens;

            var trimmed = raw.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            var token = ElementToString(el);
                            if (!string.IsNullOrWhiteSpace(token))
                                tokens.Add(token);
                        }
                        return tokens;
                    }
                }
                catch
                {
                    // fall through to string split
                }
            }

            if (trimmed.Contains("..", StringComparison.Ordinal))
            {
                var parts = trimmed.Split(new[] { ".." }, 2, StringSplitOptions.None);
                tokens.Add(parts[0].Trim());
                tokens.Add(parts[1].Trim());
                return tokens;
            }

            foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var token = part.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(token))
                    tokens.Add(token);
            }

            return tokens;
        }

        private static bool TryParseBetweenTokens(string? raw, out string min, out string max)
        {
            min = string.Empty;
            max = string.Empty;
            var tokens = ParseTokens(raw);
            if (tokens.Count < 2)
                return false;

            min = tokens[0];
            max = tokens[1];
            return true;
        }

        private static string? ElementToString(JsonElement el)
        {
            return el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Number => el.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => null,
                _ => el.GetRawText()
            };
        }

        private static HashSet<decimal> ToDecimalSet(IEnumerable<string> tokens)
        {
            var set = new HashSet<decimal>();
            foreach (var t in tokens)
            {
                if (TryParseDecimal(t, out var d))
                    set.Add(d);
            }
            return set;
        }

        private static HashSet<double> ToDoubleSet(IEnumerable<string> tokens)
        {
            var set = new HashSet<double>();
            foreach (var t in tokens)
            {
                if (TryParseDouble(t, out var d))
                    set.Add(d);
            }
            return set;
        }

        private static HashSet<DateTime> ToDateSet(IEnumerable<string> tokens)
        {
            var set = new HashSet<DateTime>();
            foreach (var t in tokens)
            {
                if (TryParseDate(t, out var d))
                    set.Add(d);
            }
            return set;
        }

        private static HashSet<bool> ToBoolSet(IEnumerable<string> tokens)
        {
            var set = new HashSet<bool>();
            foreach (var t in tokens)
            {
                if (TryParseBool(t, out var b))
                    set.Add(b);
            }
            return set;
        }

        private static bool TryParseDecimal(string? input, out decimal result)
        {
            result = 0m;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            return decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out result)
                || decimal.TryParse(input, NumberStyles.Any, CultureInfo.CurrentCulture, out result);
        }

        private static bool TryParseDouble(string? input, out double result)
        {
            result = 0d;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            return double.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out result)
                || double.TryParse(input, NumberStyles.Any, CultureInfo.CurrentCulture, out result);
        }

        private static bool TryParseDate(string? input, out DateTime result)
        {
            result = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            if (DateTime.TryParse(input, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
                return true;

            if (DateTime.TryParse(input, CultureInfo.CurrentCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
                return true;

            return false;
        }

        private static bool TryParseBool(string? input, out bool result)
        {
            result = false;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            if (bool.TryParse(input, out result))
                return true;

            if (int.TryParse(input, out var i))
            {
                result = i != 0;
                return true;
            }

            return false;
        }

        private static bool TryGetDecimalValue(object? value, out decimal result)
        {
            if (value is null)
            {
                result = 0m;
                return false;
            }

            switch (value)
            {
                case decimal dec:
                    result = dec;
                    return true;
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case double d:
                    result = (decimal)d;
                    return true;
                case float f:
                    result = (decimal)f;
                    return true;
                case string s:
                    return TryParseDecimal(s, out result);
            }

            return TryParseDecimal(value.ToString(), out result);
        }

        private static bool TryGetDoubleValue(object? value, out double result)
        {
            if (value is null)
            {
                result = 0d;
                return false;
            }

            switch (value)
            {
                case double d:
                    result = d;
                    return true;
                case float f:
                    result = f;
                    return true;
                case decimal dec:
                    result = (double)dec;
                    return true;
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case string s:
                    return TryParseDouble(s, out result);
            }

            return TryParseDouble(value.ToString(), out result);
        }

        private static bool TryGetDateValue(object? value, out DateTime result)
        {
            if (value is null)
            {
                result = DateTime.MinValue;
                return false;
            }

            if (value is DateTime dt)
            {
                result = dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime();
                return true;
            }

            if (value is DateTimeOffset dto)
            {
                result = dto.UtcDateTime;
                return true;
            }

            if (value is string s)
                return TryParseDate(s, out result);

            return TryParseDate(value.ToString(), out result);
        }

        private static bool TryGetBoolValue(object? value, out bool result)
        {
            if (value is null)
            {
                result = false;
                return false;
            }

            switch (value)
            {
                case bool b:
                    result = b;
                    return true;
                case string s:
                    return TryParseBool(s, out result);
                case int i:
                    result = i != 0;
                    return true;
                case long l:
                    result = l != 0;
                    return true;
            }

            return TryParseBool(value.ToString(), out result);
        }

        private sealed class AggState
        {
            public string[] KeyParts { get; }
            private readonly AggregateSpec[] _aggs;
            private readonly bool[] _useDecimal;
            private readonly decimal[] _decSum;
            private readonly double[] _dblSum;
            private readonly int[] _count;

            public AggState(string[] keyParts, IReadOnlyList<AggregateSpec> aggs, bool[] useDecimal)
            {
                KeyParts = keyParts;
                _aggs = aggs.ToArray();
                _useDecimal = useDecimal.ToArray();
                _decSum = new decimal[_aggs.Length];
                _dblSum = new double[_aggs.Length];
                _count = new int[_aggs.Length];
            }

            public void Accumulate(DataFrame df, long rowIndex)
            {
                for (var i = 0; i < _aggs.Length; i++)
                {
                    var a = _aggs[i];
                    if (a.Op == "count")
                    {
                        _count[i]++;
                        continue;
                    }

                    var val = df.Columns[a.Column!][rowIndex];
                    if (val is null) continue;

                    if (_useDecimal[i])
                    {
                        if (!TryGetDecimal(val, out var d)) continue;
                        switch (a.Op)
                        {
                            case "sum":
                            case "avg":
                                _decSum[i] += d;
                                _count[i]++;
                                break;
                            case "min":
                                if (_count[i] == 0) _decSum[i] = d;
                                else _decSum[i] = Math.Min(_decSum[i], d);
                                _count[i]++;
                                break;
                            case "max":
                                if (_count[i] == 0) _decSum[i] = d;
                                else _decSum[i] = Math.Max(_decSum[i], d);
                                _count[i]++;
                                break;
                            default:
                                _decSum[i] += d;
                                _count[i]++;
                                break;
                        }
                    }
                    else
                    {
                        if (!TryGetDouble(val, out var d)) continue;
                        switch (a.Op)
                        {
                            case "sum":
                            case "avg":
                                _dblSum[i] += d;
                                _count[i]++;
                                break;
                            case "min":
                                if (_count[i] == 0) _dblSum[i] = d;
                                else _dblSum[i] = Math.Min(_dblSum[i], d);
                                _count[i]++;
                                break;
                            case "max":
                                if (_count[i] == 0) _dblSum[i] = d;
                                else _dblSum[i] = Math.Max(_dblSum[i], d);
                                _count[i]++;
                                break;
                            default:
                                _dblSum[i] += d;
                                _count[i]++;
                                break;
                        }
                    }
                }
            }

            public object GetValue(int i)
            {
                var a = _aggs[i];
                if (a.Op == "count")
                    return (double)_count[i];

                if (_useDecimal[i])
                {
                    if (a.Op == "avg")
                        return _count[i] == 0 ? 0m : _decSum[i] / _count[i];
                    return _decSum[i];
                }

                if (a.Op == "avg")
                    return _count[i] == 0 ? 0d : _dblSum[i] / _count[i];

                return _dblSum[i];
            }

            private static bool TryGetDecimal(object value, out decimal result)
            {
                switch (value)
                {
                    case decimal dec:
                        result = dec;
                        return true;
                    case int i:
                        result = i;
                        return true;
                    case long l:
                        result = l;
                        return true;
                    case double d:
                        result = (decimal)d;
                        return true;
                    case float f:
                        result = (decimal)f;
                        return true;
                }

                return decimal.TryParse(value.ToString(), out result);
            }

            private static bool TryGetDouble(object value, out double result)
            {
                switch (value)
                {
                    case double d:
                        result = d;
                        return true;
                    case float f:
                        result = f;
                        return true;
                    case decimal dec:
                        result = (double)dec;
                        return true;
                    case int i:
                        result = i;
                        return true;
                    case long l:
                        result = l;
                        return true;
                }

                return double.TryParse(value.ToString(), out result);
            }
        }
    }
}
