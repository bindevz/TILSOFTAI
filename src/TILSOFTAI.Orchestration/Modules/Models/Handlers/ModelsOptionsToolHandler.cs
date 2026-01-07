using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

public sealed class ModelsOptionsToolHandler : IToolHandler
{
    public string ToolName => "models.options";

    private readonly ModelsService _modelsService;

    public ModelsOptionsToolHandler(ModelsService modelsService)
    {
        _modelsService = modelsService;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var modelId = dyn.GetInt("modelId");
        var includeConstraints = dyn.GetBool("includeConstraints", true);
        var result = await _modelsService.OptionsAsync(modelId, includeConstraints, context, cancellationToken);

        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(result.Model.ModelCode) && result.OptionGroups.Count == 0)
            warnings.Add("SP_NOT_DEPLOYED_OR_MODEL_NOT_FOUND");

        var payload = new
        {
            kind = "models.options.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.options",
            data = new
            {
                model = new
                {
                    modelId = result.Model.ModelId,
                    modelCode = result.Model.ModelCode,
                    modelName = result.Model.ModelName,
                    season = result.Model.Season,
                    collection = result.Model.Collection,
                    rangeName = result.Model.RangeName
                },
                optionGroups = result.OptionGroups.Select(g => new
                {
                    groupKey = g.GroupKey,
                    groupName = g.GroupName,
                    isRequired = g.IsRequired,
                    values = g.Values.Select(v => new { valueKey = v.ValueKey, valueName = v.ValueName, note = v.Note })
                }),
                constraints = result.Constraints.Select(c => new
                {
                    ruleType = c.RuleType,
                    ifGroupKey = c.IfGroupKey,
                    ifValueKey = c.IfValueKey,
                    thenGroupKey = c.ThenGroupKey,
                    thenValueKey = c.ThenValueKey,
                    message = c.Message
                })
            },
            warnings
        };

        var evidence = new List<EnvelopeEvidenceItemV1>
        {
            new()
            {
                Id = "ev_model",
                Type = "entity",
                Title = "Thông tin model",
                Payload = new
                {
                    result.Model.ModelId,
                    result.Model.ModelCode,
                    result.Model.ModelName,
                    result.Model.Season,
                    result.Model.Collection,
                    result.Model.RangeName
                }
            },
            new()
            {
                Id = "ev_optionGroups",
                Type = "list",
                Title = "Danh sách tùy chọn (option groups)",
                Payload = result.OptionGroups.Select(g => new
                {
                    g.GroupKey,
                    g.GroupName,
                    g.IsRequired,
                    values = g.Values.Select(v => v.ValueName)
                })
            }
        };

        if (includeConstraints)
        {
            evidence.Add(new EnvelopeEvidenceItemV1
            {
                Id = "ev_constraints",
                Type = "list",
                Title = "Ràng buộc tùy chọn (constraints)",
                Payload = result.Constraints.Select(c => new
                {
                    c.RuleType,
                    c.IfGroupKey,
                    c.IfValueKey,
                    c.ThenGroupKey,
                    c.ThenValueKey,
                    c.Message
                })
            });
        }

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1
            {
                System = "sqlserver",
                Name = "dbo.TILSOFTAI_sp_models_options_v1",
                Cache = "na",
                Note = "Model options + constraints"
            },
            Evidence: evidence);

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.options executed", payload), extras);
    }
}
