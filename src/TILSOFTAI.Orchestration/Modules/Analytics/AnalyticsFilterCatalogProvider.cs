using TILSOFTAI.Orchestration.Tools.FiltersCatalog;

namespace TILSOFTAI.Orchestration.Modules.Analytics;

/// <summary>
/// Analytics module contribution to filters.catalog.
/// In ver23 we start with the same filter surface as models.search for the dataset.create tool.
/// </summary>
public sealed class AnalyticsFilterCatalogProvider : IFilterCatalogProvider
{
    public IEnumerable<ResourceFilterCatalog> GetCatalogs()
    {
        yield return BuildDatasetCreate();
    }

    private static ResourceFilterCatalog BuildDatasetCreate()
    {
        // Reuse the "models" filter vocabulary for the initial Atomic Data Engine cut.
        return new ResourceFilterCatalog(
            Resource: "analytics.dataset.create",
            Title: "Create analytics dataset",
            Description: "Tạo dataset thô phía server để chạy phân tích (in-memory) mà không trả dữ liệu thô lớn về LLM.",
            SupportedFilters: new List<FilterDescriptor>
            {
                new("season", "string",
                    "Mùa. Chấp nhận '24/25' hoặc '2024/2025' (hệ thống sẽ normalize theo chuẩn nội bộ).",
                    new[] { "24/25", "2024/2025" },
                    Operators: new[] { "eq" },
                    Aliases: new[] { "mùa", "season" },
                    Normalize: "SeasonCode.Parse"),

                new("collection", "string",
                    "Bộ sưu tập (vd: Đông/Hè/Core...).",
                    new[] { "Đông", "Hè", "Core" },
                    Operators: new[] { "eq", "contains" },
                    Aliases: new[] { "bộ sưu tập", "collection" },
                    Normalize: "Trim"),

                new("rangeName", "string",
                    "Range/nhóm sản phẩm.",
                    new[] { "Indoor", "Outdoor" },
                    Operators: new[] { "eq", "contains" },
                    Aliases: new[] { "category", "range", "range name" },
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
                    source = "models",
                    filters = new { season = "24/25", collection = "Đông" },
                    maxRows = 20000,
                    previewRows = 100
                },
                note = "Trả về datasetId + schema + preview nhỏ. Dữ liệu thô đầy đủ được giữ server-side theo datasetId."
            }
        );
    }
}
