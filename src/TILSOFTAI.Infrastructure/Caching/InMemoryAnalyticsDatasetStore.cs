using Microsoft.Extensions.Caching.Memory;
using TILSOFTAI.Domain.Interfaces;

namespace TILSOFTAI.Infrastructure.Caching;

/// <summary>
/// In-memory dataset store backed by IMemoryCache.
/// Designed for short-lived analytic datasets (seconds/minutes), scoped by datasetId.
/// </summary>
public sealed class InMemoryAnalyticsDatasetStore : IAnalyticsDatasetStore
{
    private readonly IMemoryCache _cache;

    public InMemoryAnalyticsDatasetStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task StoreAsync(string datasetId, object dataset, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
            throw new ArgumentException("datasetId is required.", nameof(datasetId));
        _cache.Set(datasetId, dataset, ttl);
        return Task.CompletedTask;
    }

    public bool TryGet(string datasetId, out object dataset)
    {
        if (string.IsNullOrWhiteSpace(datasetId))
        {
            dataset = null!;
            return false;
        }

        return _cache.TryGetValue(datasetId, out dataset!);
    }

    public void Remove(string datasetId)
    {
        if (string.IsNullOrWhiteSpace(datasetId)) return;
        _cache.Remove(datasetId);
    }
}
