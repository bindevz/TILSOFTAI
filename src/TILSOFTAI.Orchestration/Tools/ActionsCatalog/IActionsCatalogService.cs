namespace TILSOFTAI.Orchestration.Tools.ActionsCatalog;

public interface IActionsCatalogService
{
    object Catalog(string? action = null, bool includeExamples = false);
}
