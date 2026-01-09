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
    public EngineResult Execute(DataFrame dataset, JsonElement pipeline, EngineBounds bounds)
    {
        if (dataset is null) throw new ArgumentNullException(nameof(dataset));
        bounds = bounds.WithDefaults();

        var plan = AnalysisPipeline.Parse(pipeline);
        var result = AnalysisExecutor.Execute(dataset, plan, bounds);
        return new EngineResult(result, plan.Warnings);
    }

    public sealed record EngineBounds(int TopN, int MaxGroups)
    {
        public EngineBounds WithDefaults()
        {
            var topN = TopN <= 0 ? 20 : Math.Clamp(TopN, 1, 200);
            var maxGroups = MaxGroups <= 0 ? 200 : Math.Clamp(MaxGroups, 1, 5_000);
            return new EngineBounds(topN, maxGroups);
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

            if (pipeline.ValueKind != JsonValueKind.Array)
                throw new ArgumentException("pipeline must be a JSON array.");

            foreach (var s in pipeline.EnumerateArray())
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

    internal static class AnalysisExecutor
    {
        public static DataFrame Execute(DataFrame df, AnalysisPipeline pipeline, EngineBounds bounds)
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
                }
            }

            // Enforce final TopN even if pipeline did not specify.
            if (current.Rows.Count > bounds.TopN)
                current = ApplyTopN(current, bounds.TopN);

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
                    st = new AggState(keyParts, step.Aggregates);
                    groups[key] = st;
                }

                st.Accumulate(df, i);
            }

            // Materialize result DataFrame.
            var resultColumns = new List<DataFrameColumn>();
            foreach (var by in step.By)
                resultColumns.Add(new StringDataFrameColumn(by, groups.Count));
            foreach (var a in step.Aggregates)
                resultColumns.Add(new DoubleDataFrameColumn(a.As, groups.Count));

            var outDf = new DataFrame(resultColumns);
            var row = 0;
            foreach (var g in groups.Values)
            {
                for (var k = 0; k < step.By.Count; k++)
                    ((StringDataFrameColumn)outDf.Columns[step.By[k]])[row] = g.KeyParts[k];

                for (var ai = 0; ai < step.Aggregates.Count; ai++)
                    ((DoubleDataFrameColumn)outDf.Columns[step.Aggregates[ai].As])[row] = g.GetValue(ai);

                row++;
            }

            return outDf;
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

        private static DataFrameColumn CreateEmptyColumnLike(DataFrameColumn c, long length)
        {
            // Keep only types we use in ver23. For unknown types, fall back to string.
            return c switch
            {
                Int32DataFrameColumn => new Int32DataFrameColumn(c.Name, length),
                DoubleDataFrameColumn => new DoubleDataFrameColumn(c.Name, length),
                StringDataFrameColumn => new StringDataFrameColumn(c.Name, length),
                _ => new StringDataFrameColumn(c.Name, length)
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
            private readonly double[] _sum;
            private readonly int[] _count;

            public AggState(string[] keyParts, IReadOnlyList<AggregateSpec> aggs)
            {
                KeyParts = keyParts;
                _aggs = aggs.ToArray();
                _sum = new double[_aggs.Length];
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
                    if (!double.TryParse(val.ToString(), out var d)) continue;

                    switch (a.Op)
                    {
                        case "sum":
                        case "avg":
                            _sum[i] += d;
                            _count[i]++;
                            break;
                        case "min":
                            if (_count[i] == 0) _sum[i] = d;
                            else _sum[i] = Math.Min(_sum[i], d);
                            _count[i]++;
                            break;
                        case "max":
                            if (_count[i] == 0) _sum[i] = d;
                            else _sum[i] = Math.Max(_sum[i], d);
                            _count[i]++;
                            break;
                        default:
                            // treat as sum
                            _sum[i] += d;
                            _count[i]++;
                            break;
                    }
                }
            }

            public double GetValue(int i)
            {
                var a = _aggs[i];
                return a.Op switch
                {
                    "count" => _count[i],
                    "avg" => _count[i] == 0 ? 0 : _sum[i] / _count[i],
                    _ => _sum[i]
                };
            }
        }
    }
}
