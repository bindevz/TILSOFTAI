using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Analytics.Handlers;

public sealed class AtomicCatalogSearchToolHandler : IToolHandler
{
    public string ToolName => "atomic.catalog.search";

    private readonly AtomicCatalogService _catalog;
    private readonly ILogger<AtomicCatalogSearchToolHandler> _logger;

    public AtomicCatalogSearchToolHandler(AtomicCatalogService catalog, ILogger<AtomicCatalogSearchToolHandler> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;

        var query = dyn.GetStringRequired("query");
        var topK = dyn.GetInt("topK", 5);

        _logger.LogInformation("AtomicCatalogSearch start q={Query} topK={TopK}", query, topK);
        var hits = await _catalog.SearchAsync(query, topK, cancellationToken);
        _logger.LogInformation("AtomicCatalogSearch end q={Query} hits={Hits} top1={Top1}", query, hits.Count, hits.FirstOrDefault()?.SpName);

        var items = hits.Select(h => new
        {
            spName = h.SpName,
            domain = h.Domain,
            entity = h.Entity,
            score = h.Score,
            parameters = AtomicCatalogService.ParseParamSpecs(h.ParamsJson)
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        });

        var warnings = new List<string>();

        if (!items.Any())
            warnings.Add("No catalog hit. Verify dbo.TILSOFTAI_SPCatalog has data, or add the required stored procedure.");

        // Contract guidance: enforce ParamsJson as the source of truth for tool inputs.
        warnings.Add("Contract: When calling atomic.query.execute, ONLY use parameter names declared in results[].parameters[].name (ParamsJson). Do NOT use output column names from RS1/RS2 (e.g., seasonFilter).");

        var payload = new
        {
            kind = "atomic.catalog.search.v1",
            schemaVersion = 2,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "atomic.catalog.search",
            data = new
            {
                query,
                topK,
                results = items
            },
            warnings = warnings.ToArray()
        };

        var evidence = new List<EnvelopeEvidenceItemV1>();

        // Evidence: always return a compact list of hits so the client/LLM can respond without looping.
        // Keep it small to avoid token bloat.
        var compactHits = hits.Take(Math.Clamp(topK, 1, 10)).Select(h => new
        {
            spName = h.SpName,
            // Avoid overload ambiguity: Score is int and can convert to both double/decimal.
            score = Math.Round((double)h.Score, 4),
            domain = h.Domain,
            entity = h.Entity,
            schemaHints = ToBoundedJsonOrString(h.SchemaHintsJson, maxStringLength: 1000)
        });

        evidence.Add(new EnvelopeEvidenceItemV1
        {
            Id = "catalog_hits",
            Type = "list",
            Title = "Catalog hits",
            Payload = new { query, topK, results = compactHits }
        });

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1 { System = "sqlserver", Name = "dbo.TILSOFTAI_SPCatalog", Cache = "na" },
            Evidence: evidence);

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("atomic.catalog.search executed", payload), extras);
    }

    private static object? ToBoundedJsonOrString(string? json, int maxStringLength)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        var node = TryParseBoundedJson(json, maxDepth: 4, maxArrayElements: 10, maxStringLength: 400);
        if (node is not null)
            return node;

        return json.Length <= maxStringLength ? json : json.Substring(0, maxStringLength) + "...";
    }

    private static JsonNode? TryParseBoundedJson(string json, int maxDepth, int maxArrayElements, int maxStringLength)
    {
        try
        {
            var node = JsonNode.Parse(json);
            if (node is null) return null;
            var truncated = false;
            return PruneNode(node, 0, maxDepth, maxArrayElements, maxStringLength, ref truncated);
        }
        catch
        {
            return null;
        }
    }

    private static JsonNode PruneNode(JsonNode node, int depth, int maxDepth, int maxArrayElements, int maxStringLength, ref bool truncated)
    {
        if (depth >= maxDepth)
        {
            truncated = true;
            return JsonValue.Create("[truncated]")!;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var s))
            {
                if (s.Length > maxStringLength)
                {
                    truncated = true;
                    s = s.Substring(0, maxStringLength) + "...";
                }
                return JsonValue.Create(s)!;
            }

            return value.DeepClone();
        }

        if (node is JsonArray arr)
        {
            var result = new JsonArray();
            var take = Math.Min(arr.Count, maxArrayElements);
            for (var i = 0; i < take; i++)
            {
                if (arr[i] is null)
                {
                    result.Add(null);
                    continue;
                }
                result.Add(PruneNode(arr[i]!, depth + 1, maxDepth, maxArrayElements, maxStringLength, ref truncated));
            }
            if (arr.Count > maxArrayElements)
                truncated = true;
            return result;
        }

        if (node is JsonObject obj)
        {
            var result = new JsonObject();
            foreach (var kv in obj)
            {
                if (kv.Value is null)
                {
                    result[kv.Key] = null;
                    continue;
                }
                result[kv.Key] = PruneNode(kv.Value, depth + 1, maxDepth, maxArrayElements, maxStringLength, ref truncated);
            }
            return result;
        }

        return node.DeepClone();
    }
}
