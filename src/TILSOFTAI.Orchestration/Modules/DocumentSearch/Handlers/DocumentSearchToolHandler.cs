using Microsoft.Extensions.Logging;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.DocumentSearch.Handlers;

public sealed class DocumentSearchToolHandler : IToolHandler
{
    public string ToolName => "atomic.doc.search";

    private readonly DocumentSearchService _service;
    private readonly ILogger<DocumentSearchToolHandler> _logger;

    public DocumentSearchToolHandler(DocumentSearchService service, ILogger<DocumentSearchToolHandler> logger)
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
        var query = dyn.GetStringRequired("query");
        var topK = dyn.GetInt("topK", 5);

        _logger.LogInformation("DocumentSearch start q={Query} topK={TopK}", query, topK);

        IReadOnlyList<DocumentChunkHit> hits;
        try
        {
            hits = await _service.SearchAsync(query, topK, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DocumentSearch failed: {Message}", ex.Message);
            return ToolDispatchResultFactory.Create(dyn,
                ToolExecutionResult.CreateFailure("atomic.doc.search failed", new { error = ex.Message }));
        }

        var payload = new
        {
            kind = "atomic.doc.search.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "atomic.doc.search",
            data = new
            {
                query,
                topK,
                hits = hits.Select(h => new
                {
                    h.DocId,
                    h.ChunkId,
                    h.ChunkNo,
                    h.Title,
                    h.Uri,
                    h.Snippet,
                    h.Distance
                }).ToList()
            }
        };

        var evidence = new List<EnvelopeEvidenceItemV1>
        {
            new EnvelopeEvidenceItemV1
            {
                Id = "doc_search_hits",
                Type = "list",
                Title = "Document Search Hits",
                Payload = new { query, topK, hits }
            }
        };

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1 { System = "sqlserver", Name = "dbo.TILSOFTAI_DocChunkMain", Cache = "na" },
            Evidence: evidence);

        _logger.LogInformation("DocumentSearch end hits={Hits}", hits.Count);
        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("atomic.doc.search executed", payload), extras);
    }
}
