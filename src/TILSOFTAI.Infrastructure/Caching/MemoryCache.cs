using Microsoft.Extensions.Caching.Memory;

namespace TILSOFTAI.Infrastructure.Caching;

public sealed class AppMemoryCache
{
    private readonly IMemoryCache _cache;

    public AppMemoryCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl)
    {
        if (_cache.TryGetValue(key, out T? existing) && existing is not null)
        {
            return existing;
        }

        var created = await factory();
        _cache.Set(key, created, ttl);
        return created;
    }

    public void Remove(string key) => _cache.Remove(key);
}
