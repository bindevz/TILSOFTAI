using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Analysis;
using StackExchange.Redis;
using TILSOFTAI.Domain.Interfaces;

namespace TILSOFTAI.Infrastructure.Caching;

/// <summary>
/// Redis-backed dataset store with in-memory fallback.
/// </summary>
public sealed class RedisAnalyticsDatasetStore : IAnalyticsDatasetStore
{
    private const string IndexPrefix = "dataset:index:";
    private readonly IDatabase? _db;
    private readonly InMemoryAnalyticsDatasetStore _fallback;
    private readonly AnalyticsDatasetStoreOptions _options;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<Type?> AnalyticsServiceType = new(ResolveAnalyticsServiceType);
    private static readonly Lazy<Type?> AnalyticsDatasetType = new(() =>
        AnalyticsServiceType.Value?.GetNestedType("AnalyticsDataset", BindingFlags.NonPublic));
    private static readonly Lazy<Type?> AnalyticsColumnType = new(() =>
        AnalyticsServiceType.Value?.GetNestedType("AnalyticsColumn", BindingFlags.Public));

    public RedisAnalyticsDatasetStore(IConnectionMultiplexer? mux, InMemoryAnalyticsDatasetStore fallback, AnalyticsDatasetStoreOptions options)
    {
        _db = mux?.GetDatabase();
        _fallback = fallback;
        _options = options ?? new AnalyticsDatasetStoreOptions();
    }

    public async Task StoreAsync(string datasetId, object dataset, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
            throw new ArgumentException("datasetId is required.", nameof(datasetId));

        var effectiveTtl = ResolveTtl(ttl);
        if (TryExtractDataset(dataset, effectiveTtl, out var stored))
        {
            if (_db is not null)
            {
                var key = BuildKey(stored.TenantId, stored.UserId, stored.DatasetId);
                var indexKey = BuildIndexKey(stored.DatasetId);
                var payload = JsonSerializer.Serialize(stored, _json);

                try
                {
                    var tran = _db.CreateTransaction();
                    _ = tran.StringSetAsync(key, payload, effectiveTtl);
                    _ = tran.StringSetAsync(indexKey, key, effectiveTtl);
                    await tran.ExecuteAsync();
                }
                catch
                {
                    // Fall back to in-memory only.
                }
            }
        }

        await _fallback.StoreAsync(datasetId, dataset, effectiveTtl, cancellationToken);
    }

    public bool TryGet(string datasetId, out object dataset)
    {
        dataset = null!;
        if (string.IsNullOrWhiteSpace(datasetId))
            return false;

        if (_db is not null)
        {
            try
            {
                var indexKey = BuildIndexKey(datasetId);
                var key = _db.StringGet(indexKey);
                if (!key.IsNullOrEmpty)
                {
                    var payload = _db.StringGet(key.ToString());
                    if (!payload.IsNullOrEmpty)
                    {
                        var stored = JsonSerializer.Deserialize<StoredDataset>(payload!, _json);
                        if (stored is not null &&
                            string.Equals(stored.DatasetId, datasetId, StringComparison.OrdinalIgnoreCase))
                        {
                            var expectedKey = BuildKey(stored.TenantId, stored.UserId, stored.DatasetId);
                            if (string.Equals(expectedKey, key.ToString(), StringComparison.OrdinalIgnoreCase))
                            {
                                var df = FromColumnarData(stored.Data);
                                var schema = BuildAnalyticsSchema(df);
                                if (schema is null)
                                    throw new InvalidOperationException("Analytics schema type was not available.");

                                var obj = CreateAnalyticsDataset(stored, df, schema);
                                if (obj is not null)
                                {
                                    dataset = obj;
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fall through to in-memory fallback.
            }
        }

        return _fallback.TryGet(datasetId, out dataset);
    }

    public void Remove(string datasetId)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
            return;

        _fallback.Remove(datasetId);

        if (_db is null)
            return;

        try
        {
            var indexKey = BuildIndexKey(datasetId);
            var key = _db.StringGet(indexKey);
            if (!key.IsNullOrEmpty)
                _db.KeyDelete(key.ToString());
            _db.KeyDelete(indexKey);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private TimeSpan ResolveTtl(TimeSpan ttl)
    {
        if (_options.TtlMinutes > 0)
            return TimeSpan.FromMinutes(_options.TtlMinutes);

        return ttl <= TimeSpan.Zero ? TimeSpan.FromMinutes(10) : ttl;
    }

    private static string BuildKey(string tenantId, string userId, string datasetId)
        => $"{tenantId}:{userId}:dataset:{datasetId}";

    private static string BuildIndexKey(string datasetId)
        => $"{IndexPrefix}{datasetId}";

    private static bool TryExtractDataset(object dataset, TimeSpan ttl, out StoredDataset stored)
    {
        stored = null!;
        if (dataset is null)
            return false;

        var datasetId = GetStringProperty(dataset, "DatasetId");
        var source = GetStringProperty(dataset, "Source") ?? string.Empty;
        var tenantId = GetStringProperty(dataset, "TenantId");
        var userId = GetStringProperty(dataset, "UserId");
        var createdAt = GetDateTimeOffsetProperty(dataset, "CreatedAtUtc") ?? DateTimeOffset.UtcNow;
        var schemaDigest = GetProperty<object>(dataset, "SchemaDigest");
        var df = GetProperty<DataFrame>(dataset, "Data");

        if (string.IsNullOrWhiteSpace(datasetId) ||
            string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(userId) ||
            df is null)
        {
            return false;
        }

        if (schemaDigest is JsonElement je)
            schemaDigest = je.Clone();

        var columnar = ToColumnarData(df);
        stored = new StoredDataset(
            DatasetId: datasetId!,
            Source: source,
            TenantId: tenantId!,
            UserId: userId!,
            CreatedAtUtc: createdAt,
            TtlSeconds: (int)Math.Max(1, ttl.TotalSeconds),
            SchemaDigest: schemaDigest,
            Data: columnar);

        return true;
    }

    private static ColumnarData ToColumnarData(DataFrame df)
    {
        var columns = new List<ColumnarColumn>(df.Columns.Count);
        var rowCount = (int)Math.Min(df.Rows.Count, int.MaxValue);

        foreach (var col in df.Columns)
        {
            var values = new object?[rowCount];
            for (var i = 0; i < rowCount; i++)
                values[i] = col[i];

            columns.Add(new ColumnarColumn(col.Name, col.DataType.Name, values));
        }

        return new ColumnarData(columns, rowCount);
    }

    private static DataFrame FromColumnarData(ColumnarData data)
    {
        var rowCount = data.RowCount;
        if (rowCount <= 0)
        {
            rowCount = 0;
            foreach (var col in data.Columns)
                rowCount = Math.Max(rowCount, col.Values?.Length ?? 0);
        }

        var cols = new List<DataFrameColumn>(data.Columns.Count);
        foreach (var col in data.Columns)
        {
            var values = col.Values ?? Array.Empty<object?>();
            var dfCol = CreateColumn(col.Name, col.DataType, rowCount);

            var max = Math.Min(values.Length, rowCount);
            for (var i = 0; i < max; i++)
                dfCol[i] = CoerceValue(values[i], col.DataType);

            cols.Add(dfCol);
        }

        return new DataFrame(cols);
    }

    private static DataFrameColumn CreateColumn(string name, string dataType, int rowCount)
    {
        var type = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        return type switch
        {
            "int32" => new Int32DataFrameColumn(name, rowCount),
            "int64" => new Int64DataFrameColumn(name, rowCount),
            "double" => new DoubleDataFrameColumn(name, rowCount),
            "single" => new SingleDataFrameColumn(name, rowCount),
            "decimal" => new DecimalDataFrameColumn(name, rowCount),
            "boolean" => new BooleanDataFrameColumn(name, rowCount),
            "datetime" => new DateTimeDataFrameColumn(name, rowCount),
            _ => new StringDataFrameColumn(name, rowCount)
        };
    }

    private static object? CoerceValue(object? value, string dataType)
    {
        if (value is null)
            return null;

        if (value is JsonElement je)
            return CoerceJsonElement(je, dataType);

        try
        {
            var type = (dataType ?? string.Empty).Trim().ToLowerInvariant();
            return type switch
            {
                "int32" => Convert.ToInt32(value),
                "int64" => Convert.ToInt64(value),
                "double" => Convert.ToDouble(value),
                "single" => Convert.ToSingle(value),
                "decimal" => Convert.ToDecimal(value),
                "boolean" => Convert.ToBoolean(value),
                "datetime" => Convert.ToDateTime(value),
                _ => value.ToString()
            };
        }
        catch
        {
            return value.ToString();
        }
    }

    private static object? CoerceJsonElement(JsonElement je, string dataType)
    {
        var type = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        try
        {
            return type switch
            {
                "int32" when je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var i) => i,
                "int64" when je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var l) => l,
                "double" when je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var d) => d,
                "single" when je.ValueKind == JsonValueKind.Number && je.TryGetDouble(out var f) => (float)f,
                "decimal" when je.ValueKind == JsonValueKind.Number && je.TryGetDecimal(out var dec) => dec,
                "boolean" when je.ValueKind is JsonValueKind.True or JsonValueKind.False => je.GetBoolean(),
                "datetime" when je.ValueKind == JsonValueKind.String && DateTime.TryParse(je.GetString(), out var dt) => dt,
                _ => je.ToString()
            };
        }
        catch
        {
            return je.ToString();
        }
    }

    private static object? BuildAnalyticsSchema(DataFrame df)
    {
        var columnType = AnalyticsColumnType.Value;
        if (columnType is null)
            return null;

        var listType = typeof(List<>).MakeGenericType(columnType);
        var list = (IList)Activator.CreateInstance(listType)!;

        foreach (var c in df.Columns)
        {
            var col = Activator.CreateInstance(columnType, new object?[] { c.Name, c.DataType.Name, c.Name });
            if (col is not null)
                list.Add(col);
        }

        return list;
    }

    private static object? CreateAnalyticsDataset(StoredDataset stored, DataFrame df, object schema)
    {
        var datasetType = AnalyticsDatasetType.Value;
        if (datasetType is null)
            return null;

        return Activator.CreateInstance(
            datasetType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object?[]
            {
                stored.DatasetId,
                stored.Source,
                stored.TenantId,
                stored.UserId,
                stored.CreatedAtUtc,
                df,
                schema,
                stored.SchemaDigest
            },
            culture: null);
    }

    private static Type? ResolveAnalyticsServiceType()
    {
        const string typeName = "TILSOFTAI.Application.Services.AnalyticsService";
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (t is not null)
                return t;
        }

        return null;
    }

    private static T? GetProperty<T>(object source, string name) where T : class
    {
        var prop = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop is null)
            return default;

        var value = prop.GetValue(source);
        return value as T;
    }

    private static DateTimeOffset? GetDateTimeOffsetProperty(object source, string name)
    {
        var prop = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop is null)
            return null;

        var value = prop.GetValue(source);
        return value switch
        {
            DateTimeOffset dto => dto,
            DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
            _ => null
        };
    }

    private static string? GetStringProperty(object source, string name)
        => GetProperty<object>(source, name)?.ToString();

    private sealed record StoredDataset(
        string DatasetId,
        string Source,
        string TenantId,
        string UserId,
        DateTimeOffset CreatedAtUtc,
        int TtlSeconds,
        object? SchemaDigest,
        ColumnarData Data);

    private sealed record ColumnarData(IReadOnlyList<ColumnarColumn> Columns, int RowCount);

    private sealed record ColumnarColumn(string Name, string DataType, object?[] Values);
}
