namespace TILSOFTAI.Orchestration.Tools.FiltersCatalog;

public interface IFilterCatalogService
{
    object GetCatalog(string? resource, bool includeValues);
}

public sealed class FilterCatalogService : IFilterCatalogService
{
    public object GetCatalog(string? resource, bool includeValues)
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

        // includeValues: giai đoạn 1 bạn có thể bỏ qua (return null)
        // giai đoạn 2: sẽ bổ sung query DB để trả valueHints theo tenant
        object? valueHints = null;

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
}
