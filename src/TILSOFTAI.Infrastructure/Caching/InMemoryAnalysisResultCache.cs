using Microsoft.Extensions.Caching.Memory;
using TILSOFTAI.Domain.Interfaces;

namespace TILSOFTAI.Infrastructure.Caching;

public sealed class InMemoryAnalysisResultCache : IAnalyticsResultCache
{
    private readonly IMemoryCache _cache;

    public InMemoryAnalysisResultCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task StoreAsync(string cacheKey, object result, TimeSpan ttl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(cacheKey) || result is null)
            return Task.CompletedTask;

        _cache.Set(cacheKey, result, ttl);
        return Task.CompletedTask;
    }

    public bool TryGet(string cacheKey, out object result)
    {
        result = null!;
        if (string.IsNullOrWhiteSpace(cacheKey))
            return false;

        return _cache.TryGetValue(cacheKey, out result!);
    }

    public void Remove(string cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
            return;

        _cache.Remove(cacheKey);
    }
}
