using System.Collections.ObjectModel;

namespace TILSOFTAI.Orchestration.Tools.FiltersCatalog;

public static class FilterCatalogRegistry
{
    // Key chính là resource/tool key, ví dụ: "models.search", "orders.query"
    private static readonly IReadOnlyDictionary<string, ResourceFilterCatalog> _catalogs
        = new ReadOnlyDictionary<string, ResourceFilterCatalog>(
            new Dictionary<string, ResourceFilterCatalog>(StringComparer.OrdinalIgnoreCase)
            {
                ["models.search"] = BuildModelsSearch(),
                ["models.count"] = BuildModelsCount(),
                ["orders.query"] = BuildOrdersQuery(),
                ["orders.summary"] = BuildOrdersSummary(),
                ["customers.search"] = BuildCustomersSearch()
            });

    public static IReadOnlyCollection<string> ListResources()
        => _catalogs.Keys.ToArray();

    public static bool TryGet(string resource, out ResourceFilterCatalog catalog)
        => _catalogs.TryGetValue(resource, out catalog!);

    private static ResourceFilterCatalog BuildModelsSearch()
    {
        return new ResourceFilterCatalog(
            Resource: "models.search",
            Title: "Search models",
            Description: "Tìm model theo các bộ lọc chuẩn (season/collection/range/modelCode/modelName).",
            SupportedFilters: new List<FilterDescriptor>
            {
                new("season", "string",
                    "Mùa. Chấp nhận '24/25' hoặc '2024/2025' (hệ thống sẽ normalize theo chuẩn nội bộ).",
                    new[] { "24/25", "2024/2025" },
                    Operators: new[] { "eq" },
                    Aliases: new[] { "mùa" },
                    Normalize: "SeasonCode.Parse"),

                new("collection", "string",
                    "Bộ sưu tập (vd: Đông/Hè/Core...).",
                    new[] { "Đông", "Hè", "Core" },
                    Operators: new[] { "eq", "contains" },
                    Aliases: new[] { "bộ sưu tập" },
                    Normalize: "Trim"),

                new("rangeName", "string",
                    "Range/nhóm sản phẩm.",
                    new[] { "Indoor", "Outdoor" },
                    Operators: new[] { "eq", "contains" },
                    Aliases: new[] { "category" },
                    Normalize: "Trim"),

                new("modelCode", "string",
                    "Mã model (ModelUD).",
                    new[] { "A001", "B120" },
                    Operators: new[] { "eq", "contains" },
                    Aliases: new[] { "code" },
                    Normalize: "TrimUpper"),

                new("modelName", "string",
                    "Tên model (ModelNM).",
                    new[] { "Model A", "Chair X" },
                    Operators: new[] { "contains" },
                    Aliases: new[] { "name" },
                    Normalize: "Trim")
            },
            Usage: new
            {
                inputShape = new
                {
                    filters = new { season = "24/25", collection = "Đông", modelName = "Model A" },
                    page = 1,
                    pageSize = 20
                },
                note = "filters là object key/value. Chỉ dùng các key trong supportedFilters."
            }
        );
    }

    private static ResourceFilterCatalog BuildModelsCount()
    {
        // Reuse the same supported filters as models.search
        var search = BuildModelsSearch();
        return new ResourceFilterCatalog(
            Resource: "models.count",
            Title: "Count models",
            Description: "Đếm tổng số model theo các bộ lọc chuẩn (giống models.search).",
            SupportedFilters: search.SupportedFilters,
            Usage: new
            {
                inputShape = new { filters = new { season = "24/25", collection = "Đông" } },
                note = "Dùng models.count khi user hỏi tổng số; nếu cần danh sách chi tiết thì dùng models.search."
            }
        );
    }

    private static ResourceFilterCatalog BuildOrdersQuery()
    {
        return new ResourceFilterCatalog(
            "orders.query",
            "Query orders",
            "Tra cứu danh sách đơn hàng theo khách hàng/trạng thái/khoảng ngày.",
            new List<FilterDescriptor>
            {
                new("customerId", "uuid", "Id khách hàng.", new[] { "c0a8012e-...." }, Operators: new[] { "eq" }),
                new("status", "enum", "Trạng thái đơn hàng.", new[] { "Pending", "Processing", "Completed", "Cancelled" }, Operators: new[] { "eq" }),
                new("startDate", "datetime", "Từ ngày (ISO 8601).", new[] { "2025-01-01T00:00:00Z" }, Operators: new[] { "gte" }),
                new("endDate", "datetime", "Đến ngày (ISO 8601).", new[] { "2025-03-31T23:59:59Z" }, Operators: new[] { "lte" }),
                new("season", "string", "Mùa (nếu hệ thống áp dụng cho báo cáo).", new[] { "24/25" }, Operators: new[] { "eq" }, Normalize: "SeasonCode.Parse"),
                new("metric", "string", "Chỉ số (vd: PI).", new[] { "PI" }, Operators: new[] { "eq" }, Normalize: "MetricCode.Parse")
            },
            new
            {
                inputShape = new
                {
                    filters = new
                    {
                        customerId = "uuid",
                        status = "Completed",
                        startDate = "2025-01-01T00:00:00Z",
                        endDate = "2025-03-31T23:59:59Z"
                    },
                    page = 1,
                    pageSize = 20
                }
            }
        );
    }

    private static ResourceFilterCatalog BuildOrdersSummary()
    {
        return new ResourceFilterCatalog(
            "orders.summary",
            "Orders summary",
            "Tóm tắt đơn hàng (count/total/avg/min/max) theo filter.",
            new List<FilterDescriptor>
            {
                new("customerId", "uuid", "Id khách hàng.", new[] { "c0a8012e-...." }, Operators: new[] { "eq" }),
                new("status", "enum", "Trạng thái đơn hàng.", new[] { "Completed" }, Operators: new[] { "eq" }),
                new("startDate", "datetime", "Từ ngày (ISO 8601).", new[] { "2025-01-01T00:00:00Z" }, Operators: new[] { "gte" }),
                new("endDate", "datetime", "Đến ngày (ISO 8601).", new[] { "2025-03-31T23:59:59Z" }, Operators: new[] { "lte" })
            },
            new
            {
                inputShape = new
                {
                    filters = new { customerId = "uuid", startDate = "2025-01-01T00:00:00Z", endDate = "2025-03-31T23:59:59Z" }
                }
            }
        );
    }

    private static ResourceFilterCatalog BuildCustomersSearch()
    {
        return new ResourceFilterCatalog(
            "customers.search",
            "Search customers",
            "Tìm khách hàng theo từ khóa.",
            new List<FilterDescriptor>
            {
                new("query", "string", "Từ khóa tìm kiếm (tên/email).", new[] { "nguyen", "abc@company.com" }, Operators: new[] { "contains" })
            },
            new
            {
                inputShape = new { filters = new { query = "nguyen" }, page = 1, pageSize = 20 }
            }
        );
    }

    // models.count uses the same filter schema as models.search but without paging.
    static FilterCatalogRegistry()
    {
        // Append a derived catalog for models.count for discovery.
        var dict = (Dictionary<string, ResourceFilterCatalog>)((ReadOnlyDictionary<string, ResourceFilterCatalog>)_catalogs).ToDictionary();
    }
}
