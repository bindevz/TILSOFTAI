namespace TILSOFTAI.Orchestration.Tools.Filters;

public static class ModelsFiltersCatalogProvider
{
    public static object GetV1()
    {
        return new
        {
            kind = "models.filters_catalog.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,

            // Canonical supported filters (keys)
            supportedFilters = new object[]
            {
                new {
                    key = "season",
                    type = "string",
                    description = "Mùa. Có thể nhập dạng rút gọn '24/25' hoặc đầy đủ '2024/2025'. Hệ thống sẽ normalize về dạng đầy đủ.",
                    examples = new[] { "24/25", "2024/2025" }
                },
                new {
                    key = "collection",
                    type = "string",
                    description = "Bộ sưu tập (ví dụ: Đông, Hè, Core...).",
                    examples = new[] { "Đông", "Hè", "Core" }
                },
                new {
                    key = "rangeName",
                    type = "string",
                    description = "Nhóm/Range của model (tùy theo dữ liệu hiện có).",
                    examples = new[] { "Indoor", "Outdoor" }
                },
                new {
                    key = "modelCode",
                    type = "string",
                    description = "Mã model (ModelUD).",
                    examples = new[] { "A001", "B120" }
                },
                new {
                    key = "modelName",
                    type = "string",
                    description = "Tên model (ModelNM).",
                    examples = new[] { "Model A", "Chair X" }
                }
            },

            // Aliases (LLM/user hay nói)
            aliases = new object[]
            {
                new { alias = "category", mapsTo = "rangeName" },
                new { alias = "name", mapsTo = "modelName" },

                // nếu bạn muốn hỗ trợ alias tiếng Việt trong filters object
                new { alias = "mùa", mapsTo = "season" },
                new { alias = "bo_suu_tap", mapsTo = "collection" },
                new { alias = "bộ sưu tập", mapsTo = "collection" }
            },

            // Guidance: recommended usage
            usage = new
            {
                howToUse = "Khi gọi tools models.search/models.count/models.stats, truyền filters là object theo canonical keys. Ví dụ: { filters: { season:'24/25', collection:'Đông', modelName:'Model A' } }"
            }
        };
    }
}
