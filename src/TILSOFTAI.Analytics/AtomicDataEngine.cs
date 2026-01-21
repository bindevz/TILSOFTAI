using Microsoft.Data.Analysis;
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
                        current = ApplyGroupBy(current, g, bounds.MaxGroups);
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
            var keep = new List<long>();
            for (long i = 0; i < df.Rows.Count; i++)
            {
                var v = col[i];
                var s = v?.ToString() ?? string.Empty;
                var pass = step.Operator switch
                {
                    "eq" => string.Equals(s, step.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase),
                    "contains" => s.IndexOf(step.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0,
                    _ => string.Equals(s, step.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                };

                if (pass) keep.Add(i);
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

        private static DataFrame ApplyGroupBy(DataFrame df, GroupByStep step, int maxGroups)
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
                    if (groups.Count >= maxGroups) continue; // drop beyond cap
                    st = new AggState(keyParts, step.Aggregates, aggUseDecimal);
                    groups[key] = st;
                }

                st.Accumulate(df, i);
            }

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

        private static Dictionary<string, List<long>> BuildRightIndex(
            DataFrame right,
            int[] keyIndexes,
            int maxIndexRows,
            int maxMatchesPerKey,
            List<string> warnings)
        {
            var index = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
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

        private static string BuildCompositeKey(DataFrame df, int[] keyIndexes, long rowIndex)
        {
            if (keyIndexes.Length == 1)
            {
                var v = df.Columns[keyIndexes[0]][rowIndex];
                return v?.ToString() ?? string.Empty;
            }

            var parts = new string[keyIndexes.Length];
            for (var i = 0; i < keyIndexes.Length; i++)
            {
                var v = df.Columns[keyIndexes[i]][rowIndex];
                parts[i] = v?.ToString() ?? string.Empty;
            }

            return string.Join("\u001F", parts);
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

            // Sort using row indices (stable).
            var order = Enumerable.Range(0, (int)df.Rows.Count)
                .Select(i => (i, v: df.Columns[idx][i]))
                .OrderBy(t => t.v?.ToString(), StringComparer.OrdinalIgnoreCase)
                .Select(t => (long)t.i)
                .ToList();

            if (step.Dir == "desc") order.Reverse();
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
                _ => new StringDataFrameColumn(name, length)
            };
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
