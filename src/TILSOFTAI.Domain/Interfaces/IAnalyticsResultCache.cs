namespace TILSOFTAI.Domain.Interfaces;

/// <summary>
/// Optional cache for analytics.run results to avoid recomputation on identical plans.
/// </summary>
public interface IAnalyticsResultCache
{
    Task StoreAsync(string cacheKey, object result, TimeSpan ttl, CancellationToken cancellationToken);
    bool TryGet(string cacheKey, out object result);
    void Remove(string cacheKey);
}
