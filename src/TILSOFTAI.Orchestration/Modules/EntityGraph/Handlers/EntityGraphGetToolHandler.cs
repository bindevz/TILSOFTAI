using Microsoft.Extensions.Logging;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.EntityGraph.Handlers;

public sealed class EntityGraphGetToolHandler : IToolHandler
{
    public string ToolName => "atomic.graph.get";

    private readonly EntityGraphService _service;
    private readonly ILogger<EntityGraphGetToolHandler> _logger;

    public EntityGraphGetToolHandler(EntityGraphService service, ILogger<EntityGraphGetToolHandler> logger)
    {
        _service = service;
        _logger = logger;
    }

    public async Task<ToolDispatchResult> HandleAsync(
        object intent,
        TSExecutionContext context,
        CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var graphCode = dyn.GetStringRequired("graphCode");

        _logger.LogInformation("EntityGraphGet start code={GraphCode}", graphCode);
        var def = await _service.GetByCodeAsync(graphCode, cancellationToken);
        
        if (def == null)
        {
            return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateFailure("atomic.graph.get failed", new { error = "Graph not found", graphCode }));
        }

        var payload = new
        {
            kind = "atomic.graph.get.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "atomic.graph.get",
            data = def
        };

        var digest = new
{
    graph = new
    {
        def.Summary.GraphId,
        def.Summary.GraphCode,
        def.Summary.Domain,
        def.Summary.Entity,
        def.Summary.Tags,
        def.Summary.RootSpName,
        def.Summary.DescriptionVi,
        def.Summary.DescriptionEn,
        def.Summary.UpdatedAtUtc
    },
    packs = def.Packs
        .OrderBy(p => p.SortOrder)
        .Take(50)
        .Select(p => new
        {
            p.PackCode,
            p.PackType,
            p.SpName,
            p.Tags,
            p.SortOrder,
            p.IntentVi,
            p.IntentEn,
            p.ParamsJson,
            p.ExampleJson,
            p.ProducesDatasetsJson
        })
        .ToList(),
    nodes = def.Nodes
        .Take(100)
        .Select(n => new
        {
            n.DatasetName,
            n.TableKind,
            n.Delivery,
            n.PrimaryKeyJson,
            n.IdColumnsJson,
            n.DimensionHintsJson,
            n.MeasureHintsJson,
            n.TimeColumnsJson
        })
        .ToList(),
    edges = def.Edges
        .Take(200)
        .Select(e => new
        {
            e.LeftDataset,
            e.RightDataset,
            e.LeftKeysJson,
            e.RightKeysJson,
            e.How,
            e.RightPrefix,
            e.SelectRightJson
        })
        .ToList(),
    glossary = def.Glossary
        .Take(200)
        .Select(g => new
        {
            g.Lang,
            g.Term,
            g.Canonical,
            g.Notes
        })
        .ToList()
};

var evidence = new List<EnvelopeEvidenceItemV1>
{
    new EnvelopeEvidenceItemV1
    {
        Id = "graph_definition",
        Type = "kv",
        Title = $"Graph: {graphCode}",
        Payload = digest
    }
};

var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1 { System = "sqlserver", Name = "dbo.TILSOFTAI_EntityGraphCatalog", Cache = "na" },
            Evidence: evidence);

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("atomic.graph.get executed", payload), extras);
    }
}
