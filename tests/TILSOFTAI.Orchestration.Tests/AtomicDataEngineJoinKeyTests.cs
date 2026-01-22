using System.Text.Json;
using Microsoft.Data.Analysis;
using TILSOFTAI.Analytics;
using Xunit;

namespace TILSOFTAI.Orchestration.Tests;

public sealed class AtomicDataEngineJoinKeyTests
{
    [Fact]
    public void Join_NormalizesDecimalAndInt()
    {
        var left = new DataFrame(
            new Int32DataFrameColumn("id", new int?[] { 1 }),
            new StringDataFrameColumn("leftValue", new string?[] { "L1" }));

        var right = new DataFrame(
            new DecimalDataFrameColumn("id", new decimal?[] { 1.0m }),
            new StringDataFrameColumn("val", new string?[] { "R1" }));

        var engine = new AtomicDataEngine();
        var result = engine.Execute(left, BuildJoinPipeline("right", "id", "id"), DefaultBounds(), id => id == "right" ? right : null);

        Assert.Equal(1, result.Data.Rows.Count);
        Assert.Equal("R1", result.Data.Columns["r_val"][0]);
    }

    [Fact]
    public void Join_NormalizesMixedNumericTypes()
    {
        var left = new DataFrame(
            new Int64DataFrameColumn("id", new long?[] { 42 }),
            new StringDataFrameColumn("leftValue", new string?[] { "L1" }));

        var right = new DataFrame(
            new DoubleDataFrameColumn("id", new double?[] { 42.0 }),
            new StringDataFrameColumn("val", new string?[] { "R1" }));

        var engine = new AtomicDataEngine();
        var result = engine.Execute(left, BuildJoinPipeline("right", "id", "id"), DefaultBounds(), id => id == "right" ? right : null);

        Assert.Equal(1, result.Data.Rows.Count);
        Assert.Equal("R1", result.Data.Columns["r_val"][0]);
    }

    [Fact]
    public void Join_NormalizesDateKeys()
    {
        var leftDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var rightDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var left = new DataFrame(
            new PrimitiveDataFrameColumn<DateTime>("dt", new[] { leftDate }),
            new StringDataFrameColumn("leftValue", new string?[] { "L1" }));

        var right = new DataFrame(
            new PrimitiveDataFrameColumn<DateTime>("dt", new[] { rightDate }),
            new StringDataFrameColumn("val", new string?[] { "R1" }));

        var engine = new AtomicDataEngine();
        var result = engine.Execute(left, BuildJoinPipeline("right", "dt", "dt"), DefaultBounds(), id => id == "right" ? right : null);

        Assert.Equal(1, result.Data.Rows.Count);
        Assert.Equal("R1", result.Data.Columns["r_val"][0]);
    }

    private static AtomicDataEngine.EngineBounds DefaultBounds()
        => new(TopN: 100, MaxGroups: 100, MaxJoinRows: 1000, MaxJoinMatchesPerLeft: 10, MaxColumns: 50, MaxResultRows: 1000);

    private static JsonElement BuildJoinPipeline(string rightDatasetId, string leftKey, string rightKey)
    {
        var payload = new[]
        {
            new
            {
                op = "join",
                rightDatasetId,
                leftKeys = new[] { leftKey },
                rightKeys = new[] { rightKey },
                how = "inner",
                rightPrefix = "r_",
                selectRight = new[] { "val" }
            }
        };

        return JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(payload));
    }
}
