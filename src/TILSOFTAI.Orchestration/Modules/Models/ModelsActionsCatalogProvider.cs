using TILSOFTAI.Orchestration.Tools.ActionsCatalog;

namespace TILSOFTAI.Orchestration.Modules.Models;

/// <summary>
/// Models module contribution to actions.catalog.
/// </summary>
public sealed class ModelsActionsCatalogProvider : IActionsCatalogProvider
{
    public IEnumerable<ActionDescriptor> GetActions()
    {
        yield return new ActionDescriptor(
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
        );
    }
}
