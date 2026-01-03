namespace TILSOFTAI.Orchestration.Tools.ActionsCatalog;

/// <summary>
/// Enterprise catalog of WRITE actions. This is the write-side complement of filters.catalog.
/// LLM can consult this to know:
/// - Which prepare/commit tools exist
/// - What parameters are required
/// - Example argument objects
/// </summary>
public static class ActionCatalogRegistry
{
    private static readonly Dictionary<string, ActionDescriptor> _actions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["models.create"] = new ActionDescriptor(
            Action: "models.create",
            PrepareTool: "models.create.prepare",
            CommitTool: "models.create.commit",
            Description: "Tạo model mới (2 bước: chuẩn bị -> xác nhận -> commit).",
            Parameters: new List<ActionParam>
            {
                new("name", "string", true, "Tên model"),
                new("category", "string", true, "Nhóm sản phẩm (vd: chair, table, set)"),
                new("basePrice", "decimal", true, "Giá cơ bản"),
                new("attributes", "object<string,string>", false, "Thuộc tính bổ sung")
            },
            ExamplePrepareArgs: new
            {
                name = "Model A",
                category = "chair",
                basePrice = 199.99,
                attributes = new Dictionary<string, string> { ["Frame"] = "Wood", ["Color"] = "Black" }
            },
            ExampleCommitArgs: new { confirmationId = "<confirmation_id>" }
        )
    };

    public static IReadOnlyCollection<ActionDescriptor> List() => _actions.Values.ToList();

    public static bool TryGet(string action, out ActionDescriptor descriptor)
        => _actions.TryGetValue(action, out descriptor!);
}
