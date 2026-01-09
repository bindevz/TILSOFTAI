using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

/// <summary>
/// Returns a single model header as TabularData (schema-carrying dataset) to avoid model DTO projections.
/// The modelId is the integer ModelID (from models.search / models.options).
/// </summary>
public sealed class ModelsGetToolHandler : IToolHandler
{
    public string ToolName => "models.get";

    private readonly ModelsService _modelsService;

    public ModelsGetToolHandler(ModelsService modelsService)
    {
        _modelsService = modelsService;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var modelId = dyn.GetInt("modelId");
        if (modelId <= 0) throw new ArgumentException("modelId must be a positive integer.");

        // Reuse the enterprise options SP to obtain a stable header (and degrade gracefully if SP not deployed).
        var options = await _modelsService.OptionsAsync(modelId, includeConstraints: false, context, cancellationToken);
        var m = options.Model;

        var table = new TabularData(
            Columns: new[]
            {
                new TabularColumn("modelId", TabularType.Int32),
                new TabularColumn("modelCode", TabularType.String),
                new TabularColumn("modelName", TabularType.String),
                new TabularColumn("season", TabularType.String),
                new TabularColumn("collection", TabularType.String),
                new TabularColumn("rangeName", TabularType.String)
            },
            Rows: new[]
            {
                new object?[] { m.ModelId, m.ModelCode, m.ModelName, m.Season, m.Collection, m.RangeName }
            },
            TotalCount: 1);

        var payload = new
        {
            kind = "models.get.v2",
            schemaVersion = 2,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.get",
            data = new { table },
            warnings = Array.Empty<string>()
        };

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.get executed", payload));
    }
}
