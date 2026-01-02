using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Orchestration.Tools.FiltersCatalog;

public interface IFilterCatalogService
{
    Task<object> GetCatalogAsync(TSExecutionContext context, string? resource, bool includeValues, CancellationToken cancellationToken);
}

public sealed class FilterCatalogService : IFilterCatalogService
{
    private readonly IFilterValueHintsRepository _hintsRepository;
    private readonly IAppCache _cache;

    public FilterCatalogService(IFilterValueHintsRepository hintsRepository, IAppCache cache)
    {
        _hintsRepository = hintsRepository;
        _cache = cache;
    }

    public async Task<object> GetCatalogAsync(TSExecutionContext context, string? resource, bool includeValues, CancellationToken cancellationToken)
    {
        // resource null => list all resources
        if (string.IsNullOrWhiteSpace(resource))
        {
            return new
            {
                kind = "filters.catalog.index.v1",
                schemaVersion = 1,
                generatedAtUtc = DateTimeOffset.UtcNow,
                resources = FilterCatalogRegistry.ListResources()
            };
        }

        if (!FilterCatalogRegistry.TryGet(resource, out var catalog))
        {
            return new
            {
                kind = "filters.catalog.v1",
                schemaVersion = 1,
                generatedAtUtc = DateTimeOffset.UtcNow,
                resource,
                error = new { code = "RESOURCE_NOT_SUPPORTED", message = "Resource not found in filters catalog registry." }
            };
        }

        object? valueHints = null;
        if (includeValues)
        {
            // Enterprise (Phase 2): populate valueHints via stored procedure.
            // Cached per tenant + resource to keep latency predictable.
            var cacheKey = $"filters:catalog:valueHints:v1:{context.TenantId}:{catalog.Resource}";
            var rows = await _cache.GetOrAddAsync(
                cacheKey,
                async () => await _hintsRepository.GetValueHintsAsync(context.TenantId, catalog.Resource, top: 10, cancellationToken),
                ttl: TimeSpan.FromMinutes(10));

            valueHints = BuildValueHints(rows);
        }

        return new
        {
            kind = "filters.catalog.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = catalog.Resource,
            title = catalog.Title,
            description = catalog.Description,
            supportedFilters = catalog.SupportedFilters,
            usage = catalog.Usage,
            valueHints
        };
    }

    private static object BuildValueHints(IReadOnlyList<FilterValueHintRow> rows)
    {
        // Shape optimized for LLM consumption: { key: [{value,count,label?}, ...], ... }
        var dict = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!dict.TryGetValue(row.FilterKey, out var list))
            {
                list = new List<object>();
                dict[row.FilterKey] = list;
            }

            list.Add(new { value = row.Value, count = row.Count, label = row.Label });
        }

        return new
        {
            kind = "filters.valueHints.v1",
            schemaVersion = 1,
            hints = dict
        };
    }
}
