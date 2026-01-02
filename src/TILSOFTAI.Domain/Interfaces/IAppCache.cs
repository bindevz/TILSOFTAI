namespace TILSOFTAI.Domain.Interfaces;

/// <summary>
/// Small, application-wide cache abstraction to avoid referencing infrastructure types from orchestration.
/// </summary>
public interface IAppCache
{
    Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan ttl);
    void Remove(string key);
}
