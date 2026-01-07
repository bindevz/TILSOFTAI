using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Tools;
using TILSOFTAI.Orchestration.Tools.Filters;
using TILSOFTAI.Orchestration.Tools.Modularity;

namespace TILSOFTAI.Orchestration.Modules.Models.Handlers;

public sealed class ModelsStatsToolHandler : IToolHandler
{
    public string ToolName => "models.stats";

    private readonly ModelsService _modelsService;
    private readonly IFilterCanonicalizer _canonicalizer;

    public ModelsStatsToolHandler(ModelsService modelsService, IFilterCanonicalizer canonicalizer)
    {
        _modelsService = modelsService;
        _canonicalizer = canonicalizer;
    }

    public async Task<ToolDispatchResult> HandleAsync(object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var dyn = (DynamicToolIntent)intent;
        var (filtersApplied, rejected) = _canonicalizer.Canonicalize("models.stats", dyn.Filters);
        if (filtersApplied.TryGetValue("season", out var seasonRaw))
        {
            filtersApplied["season"] = SeasonNormalizer.NormalizeToFull(seasonRaw);
        }

        var topN = dyn.GetInt("topN", 10);
        var result = await _modelsService.StatsAsync(filtersApplied, topN, context, cancellationToken);

        object? TopOf(string dimension)
        {
            var bd = result.Breakdowns.FirstOrDefault(b => string.Equals(b.Dimension, dimension, StringComparison.OrdinalIgnoreCase));
            var top = bd?.Items?.OrderByDescending(i => i.Count).FirstOrDefault();
            return top is null ? null : new { key = top.Key, label = top.Label, count = top.Count };
        }

        var warnings = new List<string>();
        if (result.Breakdowns.Count == 0)
            warnings.Add("SP_NOT_DEPLOYED_OR_NO_BREAKDOWN");

        var payload = new
        {
            kind = "models.stats.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.stats",
            filtersApplied,
            rejectedFilters = rejected,
            data = new
            {
                totalCount = result.TotalCount,
                breakdowns = result.Breakdowns.Select(b => new
                {
                    dimension = b.Dimension,
                    title = b.Title,
                    items = b.Items.Select(i => new { key = i.Key, label = i.Label, count = i.Count })
                }),
                highlights = new
                {
                    topRangeName = TopOf("rangeName"),
                    topCollection = TopOf("collection"),
                    topSeason = TopOf("season")
                }
            },
            warnings
        };

        var evidence = new List<EnvelopeEvidenceItemV1>
        {
            new()
            {
                Id = "ev_totalCount",
                Type = "metric",
                Title = "Tổng số model (theo bộ lọc)",
                Payload = new { totalCount = result.TotalCount, filtersApplied }
            }
        };

        foreach (var b in result.Breakdowns.Take(5))
        {
            evidence.Add(new EnvelopeEvidenceItemV1
            {
                Id = $"ev_breakdown_{b.Dimension}",
                Type = "breakdown",
                Title = $"Phân bổ theo {b.Title}",
                Payload = new
                {
                    dimension = b.Dimension,
                    title = b.Title,
                    top = b.Items.OrderByDescending(i => i.Count).Take(10).Select(i => new { i.Key, i.Label, i.Count })
                }
            });
        }

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1
            {
                System = "sqlserver",
                Name = "dbo.TILSOFTAI_sp_models_stats",
                Cache = "na",
                Note = "Aggregation SP"
            },
            Evidence: evidence);

        return ToolDispatchResultFactory.Create(dyn, ToolExecutionResult.CreateSuccess("models.stats executed", payload), extras);
    }
}
