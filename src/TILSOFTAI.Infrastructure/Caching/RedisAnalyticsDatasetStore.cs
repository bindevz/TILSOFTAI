using System.Text.Json;
using StackExchange.Redis;
using TILSOFTAI.Application.Analytics;
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

        if (dataset is AnalyticsDatasetDto dto)
        {
            if (_db is not null)
            {
                var key = BuildKey(dto.TenantId, dto.UserId, dto.DatasetId);
                var indexKey = BuildIndexKey(dto.DatasetId);
                var payload = JsonSerializer.Serialize(new StoredDataset(dto, (int)Math.Max(1, effectiveTtl.TotalSeconds)), _json);

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
                        var dto = stored?.Dataset;
                        if (dto is not null && string.Equals(dto.DatasetId, datasetId, StringComparison.OrdinalIgnoreCase))
                        {
                            var expectedKey = BuildKey(dto.TenantId, dto.UserId, dto.DatasetId);
                            if (string.Equals(expectedKey, key.ToString(), StringComparison.OrdinalIgnoreCase))
                            {
                                dataset = dto;
                                return true;
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

    private sealed record StoredDataset(
        AnalyticsDatasetDto Dataset,
        int TtlSeconds);
}
