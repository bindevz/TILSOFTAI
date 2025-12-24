using Microsoft.Extensions.Caching.Memory;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Infrastructure.Caching;

public sealed class MemoryConfirmationPlanStore : IConfirmationPlanStore
{
    private readonly IMemoryCache _cache;

    public MemoryConfirmationPlanStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task SaveAsync(ConfirmationPlan plan, CancellationToken cancellationToken)
    {
        var entryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = plan.ExpiresAt
        };
        _cache.Set(plan.Id, plan, entryOptions);
        return Task.CompletedTask;
    }

    public Task<ConfirmationPlan?> GetAsync(string id, CancellationToken cancellationToken)
    {
        _cache.TryGetValue(id, out ConfirmationPlan? plan);
        return Task.FromResult(plan);
    }

    public Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        _cache.Remove(id);
        return Task.CompletedTask;
    }
}
