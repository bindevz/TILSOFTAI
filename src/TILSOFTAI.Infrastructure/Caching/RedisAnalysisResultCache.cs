using System.Collections;
using System.Reflection;
using System.Linq;
using System.Text.Json;
using StackExchange.Redis;
using TILSOFTAI.Domain.Interfaces;

namespace TILSOFTAI.Infrastructure.Caching;

/// <summary>
/// Redis-backed cache for analytics.run results with in-memory fallback.
/// </summary>
public sealed class RedisAnalysisResultCache : IAnalyticsResultCache
{
    private const string KeyPrefix = "analysis:result:";
    private readonly IDatabase? _db;
    private readonly InMemoryAnalysisResultCache _fallback;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private static readonly Lazy<Type?> AnalyticsServiceType = new(ResolveAnalyticsServiceType);
    private static readonly Lazy<Type?> RunResultType = new(() =>
        AnalyticsServiceType.Value?.GetNestedType("RunResult", BindingFlags.Public));
    private static readonly Lazy<Type?> AnalyticsColumnType = new(() =>
        AnalyticsServiceType.Value?.GetNestedType("AnalyticsColumn", BindingFlags.Public));

    public RedisAnalysisResultCache(IConnectionMultiplexer? mux, InMemoryAnalysisResultCache fallback)
    {
        _db = mux?.GetDatabase();
        _fallback = fallback;
    }

    public async Task StoreAsync(string cacheKey, object result, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cacheKey) || result is null)
            return;

        if (TryExtractResult(result, out var stored))
        {
            if (_db is not null)
            {
                var key = BuildKey(cacheKey);
                var payload = JsonSerializer.Serialize(stored, _json);
                try
                {
                    await _db.StringSetAsync(key, payload, ttl);
                }
                catch
                {
                    // Fall back to in-memory cache only.
                }
            }
        }

        await _fallback.StoreAsync(cacheKey, result, ttl, cancellationToken);
    }

    public bool TryGet(string cacheKey, out object result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(cacheKey))
            return false;

        if (_db is not null)
        {
            try
            {
                var payload = _db.StringGet(BuildKey(cacheKey));
                if (!payload.IsNullOrEmpty)
                {
                    var stored = JsonSerializer.Deserialize<StoredAnalysisResult>(payload!, _json);
                    if (stored is not null)
                    {
                        var obj = CreateRunResult(stored);
                        if (obj is not null)
                        {
                            result = obj;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to in-memory cache.
            }
        }

        return _fallback.TryGet(cacheKey, out result);
    }

    public void Remove(string cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
            return;

        _fallback.Remove(cacheKey);

        if (_db is null)
            return;

        try
        {
            _db.KeyDelete(BuildKey(cacheKey));
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    private static string BuildKey(string cacheKey) => $"{KeyPrefix}{cacheKey}";

    private static bool TryExtractResult(object result, out StoredAnalysisResult stored)
    {
        stored = null!;
        if (result is null)
            return false;

        var datasetId = GetStringProperty(result, "DatasetId");
        var rowCount = GetIntProperty(result, "RowCount");
        var columnCount = GetIntProperty(result, "ColumnCount");
        var schemaObj = GetProperty<object>(result, "Schema");
        var rowsObj = GetProperty<object>(result, "Rows");
        var warningsObj = GetProperty<object>(result, "Warnings");
        var resultDatasetId = GetStringProperty(result, "ResultDatasetId");

        if (string.IsNullOrWhiteSpace(datasetId) || schemaObj is null || rowsObj is null || warningsObj is null)
            return false;

        var schema = ExtractSchema(schemaObj);
        var rows = ExtractRows(rowsObj);
        var warnings = ExtractWarnings(warningsObj);

        stored = new StoredAnalysisResult(
            DatasetId: datasetId!,
            RowCount: rowCount,
            ColumnCount: columnCount,
            Schema: schema,
            Rows: rows,
            Warnings: warnings,
            ResultDatasetId: resultDatasetId);

        return true;
    }

    private static List<StoredColumn> ExtractSchema(object schemaObj)
    {
        var list = new List<StoredColumn>();
        if (schemaObj is not IEnumerable enumerable)
            return list;

        foreach (var item in enumerable)
        {
            if (item is null) continue;
            var name = GetStringProperty(item, "Name") ?? string.Empty;
            var dataType = GetStringProperty(item, "DataType") ?? string.Empty;
            var displayName = GetStringProperty(item, "DisplayName") ?? name;
            if (!string.IsNullOrWhiteSpace(name))
                list.Add(new StoredColumn(name, dataType, displayName));
        }

        return list;
    }

    private static List<object?[]> ExtractRows(object rowsObj)
    {
        var rows = new List<object?[]>();
        if (rowsObj is IReadOnlyList<object?[]> typed)
        {
            rows.AddRange(typed);
            return rows;
        }

        if (rowsObj is IEnumerable enumerable)
        {
            foreach (var row in enumerable)
            {
                if (row is null) continue;
                if (row is object?[] arr)
                {
                    rows.Add(arr);
                    continue;
                }

                if (row is IEnumerable rowEnum && row is not string)
                {
                    rows.Add(rowEnum.Cast<object?>().ToArray());
                }
            }
        }

        return rows;
    }

    private static List<string> ExtractWarnings(object warningsObj)
    {
        var warnings = new List<string>();
        if (warningsObj is IEnumerable enumerable)
        {
            foreach (var w in enumerable)
            {
                if (w is null) continue;
                warnings.Add(w.ToString() ?? string.Empty);
            }
        }
        return warnings;
    }

    private static object? CreateRunResult(StoredAnalysisResult stored)
    {
        var runType = RunResultType.Value;
        var columnType = AnalyticsColumnType.Value;
        if (runType is null || columnType is null)
            return null;

        var listType = typeof(List<>).MakeGenericType(columnType);
        var schemaList = (IList)Activator.CreateInstance(listType)!;
        foreach (var c in stored.Schema)
        {
            var col = Activator.CreateInstance(columnType, new object?[] { c.Name, c.DataType, c.DisplayName });
            if (col is not null)
                schemaList.Add(col);
        }

        var ctor = runType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        if (ctor is null)
            return null;

        var args = new object?[ctor.GetParameters().Length];
        if (args.Length > 0) args[0] = stored.DatasetId;
        if (args.Length > 1) args[1] = stored.RowCount;
        if (args.Length > 2) args[2] = stored.ColumnCount;
        if (args.Length > 3) args[3] = schemaList;
        if (args.Length > 4) args[4] = stored.Rows;
        if (args.Length > 5) args[5] = stored.Warnings;
        if (args.Length > 6) args[6] = stored.ResultDatasetId;

        return ctor.Invoke(args);
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

    private static string? GetStringProperty(object source, string name)
        => GetProperty<object>(source, name)?.ToString();

    private static int GetIntProperty(object source, string name)
    {
        var prop = source.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop is null)
            return 0;

        var value = prop.GetValue(source);
        return value switch
        {
            int i => i,
            long l => (int)l,
            _ => int.TryParse(value?.ToString(), out var parsed) ? parsed : 0
        };
    }

    private sealed record StoredAnalysisResult(
        string DatasetId,
        int RowCount,
        int ColumnCount,
        IReadOnlyList<StoredColumn> Schema,
        IReadOnlyList<object?[]> Rows,
        IReadOnlyList<string> Warnings,
        string? ResultDatasetId);

    private sealed record StoredColumn(string Name, string DataType, string DisplayName);
}
