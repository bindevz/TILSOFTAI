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
                ["models.stats"] = BuildModelsStats(),
                ["models.options"] = BuildModelsOptions()
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

    private static ResourceFilterCatalog BuildModelsStats()
    {
        // Same filters as search/count, but output is an aggregation (contract v1).
        var search = BuildModelsSearch();
        return new ResourceFilterCatalog(
            Resource: "models.stats",
            Title: "Models statistics",
            Description: "Thống kê model theo nhiều chiều (rangeName/collection/season). Trả contract models.stats.v1.",
            SupportedFilters: search.SupportedFilters,
            Usage: new
            {
                inputShape = new { filters = new { season = "24/25" }, topN = 10 },
                note = "Dùng khi user hỏi thống kê: tổng số + top theo range/collection/season." 
            }
        );
    }

    private static ResourceFilterCatalog BuildModelsOptions()
    {
        // This resource does not accept 'filters' (it accepts modelId).
        return new ResourceFilterCatalog(
            Resource: "models.options",
            Title: "Model options",
            Description: "Lấy đầy đủ nhóm tuỳ chọn và giá trị cho 1 model. Trả contract models.options.v1.",
            SupportedFilters: new List<FilterDescriptor>(),
            Usage: new
            {
                inputShape = new { modelId = 123, includeConstraints = true },
                note = "modelId lấy từ models.search (ModelID)."
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
