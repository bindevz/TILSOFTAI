using TILSOFTAI.Application.Services;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Orchestration.Chat;
using TILSOFTAI.Orchestration.Contracts;
using TILSOFTAI.Orchestration.Llm;
using TILSOFTAI.Orchestration.Tools.FiltersCatalog;
using TILSOFTAI.Orchestration.Tools.Filters;
using TILSOFTAI.Orchestration.Tools.ActionsCatalog;

namespace TILSOFTAI.Orchestration.Tools;

public sealed class ToolDispatcher
{
    private readonly ModelsService _modelsService;
    private readonly IFilterCatalogService _filterCatalogService;
    private readonly IActionsCatalogService _actionsCatalogService;

    public ToolDispatcher(
        ModelsService modelsService,
        IFilterCatalogService filterCatalogService,
        IActionsCatalogService actionsCatalogService)
    {
        _modelsService = modelsService;
        _filterCatalogService = filterCatalogService;
        _actionsCatalogService = actionsCatalogService;
    }

    public async Task<ToolDispatchResult> DispatchAsync(string toolName, object intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        return toolName.ToLowerInvariant() switch
        {
            // READ tools use enterprise DynamicToolIntent
            "models.search" => await HandleModelsSearchAsync((DynamicToolIntent)intent, context, cancellationToken),
            "models.count" => await HandleModelsCountAsync((DynamicToolIntent)intent, context, cancellationToken),
            "models.stats" => await HandleModelsStatsAsync((DynamicToolIntent)intent, context, cancellationToken),
            "models.options" => await HandleModelsOptionsAsync((DynamicToolIntent)intent, context, cancellationToken),
            "models.get" => await HandleModelsGetAsync((DynamicToolIntent)intent, context, cancellationToken),
            "models.attributes.list" => await HandleModelsAttributesAsync((DynamicToolIntent)intent, context, cancellationToken),
            "models.price.analyze" => await HandleModelsPriceAsync((DynamicToolIntent)intent, context, cancellationToken),
            "models.create.prepare" => await HandleModelsCreatePrepareAsync((DynamicToolIntent)intent, context, cancellationToken),
            "models.create.commit" => await HandleModelsCreateCommitAsync((DynamicToolIntent)intent, context, cancellationToken),

            "filters.catalog" => await HandleFiltersCatalogAsync((DynamicToolIntent)intent, context, cancellationToken),
            "actions.catalog" => await HandleActionsCatalogAsync((DynamicToolIntent)intent, context, cancellationToken),

            _ => throw new ResponseContractException("Tool not supported.")
        };
    }

    private async Task<ToolDispatchResult> HandleModelsSearchAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var (filtersApplied, rejected) = CanonicalizeFilters("models.search", intent.Filters);
        if (filtersApplied.TryGetValue("season", out var seasonRaw))
        {
            filtersApplied["season"] = SeasonNormalizer.NormalizeToFull(seasonRaw);
        }

        var result = await _modelsService.SearchAsync(
            context.TenantId,
            filtersApplied.GetValueOrDefault("rangeName"),
            filtersApplied.GetValueOrDefault("modelCode"),
            filtersApplied.GetValueOrDefault("modelName"),
            filtersApplied.GetValueOrDefault("season"),
            filtersApplied.GetValueOrDefault("collection"),
            intent.Page,
            intent.PageSize,
            context,
            cancellationToken);

        var payload = new
        {
            kind = "models.search.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.search",
            filtersApplied,
            rejectedFilters = rejected,
            data = new
            {
                totalCount = result.TotalCount,
                pageNumber = result.PageNumber,
                pageSize = result.PageSize,
                items = result.Items.Select(m => new { m.ModelID, m.ModelUD, m.ModelNM, m.Season, m.Collection, m.RangeName })
            },
            warnings = Array.Empty<string>()
        };

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.search executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelsCountAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var (filtersApplied, rejected) = CanonicalizeFilters("models.search", intent.Filters);
        if (filtersApplied.TryGetValue("season", out var seasonRaw))
        {
            filtersApplied["season"] = SeasonNormalizer.NormalizeToFull(seasonRaw);
        }

        // Reuse the search stored procedure: request 1 row, read TotalCount.
        var result = await _modelsService.SearchAsync(
            context.TenantId,
            filtersApplied.GetValueOrDefault("rangeName"),
            filtersApplied.GetValueOrDefault("modelCode"),
            filtersApplied.GetValueOrDefault("modelName"),
            filtersApplied.GetValueOrDefault("season"),
            filtersApplied.GetValueOrDefault("collection"),
            page: 1,
            size: 1,
            context: context,
            cancellationToken: cancellationToken);

        var payload = new
        {
            kind = "models.count.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.count",
            filtersApplied,
            rejectedFilters = rejected,
            data = new
            {
                totalCount = result.TotalCount
            },
            warnings = Array.Empty<string>()
        };

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1
            {
                System = "sqlserver",
                Name = "dbo.TILSOFTAI_sp_models_search",
                Cache = "na",
                Note = "Count uses search SP (pageSize=1)"
            },
            Evidence: new[]
            {
                new EnvelopeEvidenceItemV1
                {
                    Id = "ev_totalCount",
                    Type = "metric",
                    Title = "Tổng số model (theo bộ lọc)",
                    Payload = new { totalCount = result.TotalCount, filtersApplied }
                }
            });

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.count executed", payload), extras);
    }

    private async Task<ToolDispatchResult> HandleModelsStatsAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var (filtersApplied, rejected) = CanonicalizeFilters("models.search", intent.Filters);
        if (filtersApplied.TryGetValue("season", out var seasonRaw))
        {
            filtersApplied["season"] = SeasonNormalizer.NormalizeToFull(seasonRaw);
        }

        var topN = intent.GetInt("topN", 10);
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

        // Enterprise stage-2: source + evidence registry
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
                    items = b.Items.OrderByDescending(i => i.Count).Take(10).Select(i => new { key = i.Key, label = i.Label, count = i.Count })
                }
            });
        }

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1
            {
                System = "sqlserver",
                Name = "dbo.TILSOFTAI_sp_models_stats_v1",
                Cache = "na",
                Note = "Statistics + breakdowns"
            },
            Evidence: evidence);

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.stats executed", payload), extras);
    }

    private async Task<ToolDispatchResult> HandleModelsOptionsAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var modelId = intent.GetInt("modelId");
        var includeConstraints = intent.GetBool("includeConstraints", true);
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

        var evidence2 = new List<EnvelopeEvidenceItemV1>
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
            evidence2.Add(new EnvelopeEvidenceItemV1
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

        var extras2 = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1
            {
                System = "sqlserver",
                Name = "dbo.TILSOFTAI_sp_models_options_v1",
                Cache = "na",
                Note = "Model options + constraints"
            },
            Evidence: evidence2);

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.options executed", payload), extras2);
    }

    private async Task<ToolDispatchResult> HandleModelsGetAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var modelId = intent.GetGuid("modelId");
        var model = await _modelsService.GetAsync(modelId, context, cancellationToken);
        var payload = new
        {
            kind = "models.get.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.get",
            data = new
            {
                model.ModelID,
                model.ModelUD,
                model.ModelNM,
                model.Season,
                model.Collection,
                model.RangeName
            },
            warnings = Array.Empty<string>()
        };

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.get executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelsAttributesAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var modelId = intent.GetGuid("modelId");
        var attrs = await _modelsService.ListAttributesAsync(modelId, context, cancellationToken);
        var payload = new
        {
            kind = "models.attributes.list.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.attributes.list",
            data = new
            {
                modelId,
                attributes = attrs.Select(a => new { a.Name, a.Value })
            },
            warnings = Array.Empty<string>()
        };
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.attributes.list executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelsPriceAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var modelId = intent.GetGuid("modelId");
        var analysis = await _modelsService.AnalyzePriceAsync(modelId, context, cancellationToken);
        var payload = new
        {
            kind = "models.price.analyze.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.price.analyze",
            data = analysis,
            warnings = Array.Empty<string>()
        };
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.price.analyze executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelsCreatePrepareAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var name = intent.GetStringRequired("name");
        var category = intent.GetStringRequired("category");
        var basePrice = intent.GetDecimal("basePrice");
        var attributes = intent.GetStringMap("attributes");

        var result = await _modelsService.PrepareCreateAsync(name, category, basePrice, attributes, context, cancellationToken);
        var payload = new
        {
            kind = "models.create.prepare.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.create.prepare",
            data = result,
            warnings = Array.Empty<string>()
        };
        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.create.prepare executed", payload));
    }

    private async Task<ToolDispatchResult> HandleModelsCreateCommitAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var confirmationId = intent.GetStringRequired("confirmationId");
        var created = await _modelsService.CommitCreateAsync(confirmationId, context, cancellationToken);

        var payload = new
        {
            kind = "models.create.commit.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "models.create.commit",
            data = new
            {
                created.ModelID,
                created.ModelUD,
                created.ModelNM
            },
            warnings = Array.Empty<string>()
        };

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("models.create.commit executed", payload));
    }

    private async Task<ToolDispatchResult> HandleFiltersCatalogAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var resource = intent.GetString("resource");
        var includeValues = intent.GetBool("includeValues", false);
        var catalog = await _filterCatalogService.GetCatalogAsync(context, resource, includeValues, cancellationToken);
        var payload = new
        {
            kind = "filters.catalog.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "filters.catalog",
            data = catalog,
            warnings = Array.Empty<string>()
        };

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1
            {
                System = "registry",
                Name = "FilterCatalogRegistry",
                Cache = "hit",
                Note = "In-memory filter registry"
            },
            Evidence: new[]
            {
                new EnvelopeEvidenceItemV1
                {
                    Id = "ev_filters_catalog",
                    Type = "list",
                    Title = "Danh sách filters hợp lệ",
                    Payload = new { resource = resource ?? string.Empty, includeValues, catalog }
                }
            });

        return CreateResult(intent, ToolExecutionResult.CreateSuccess("filters.catalog executed", payload), extras);
    }

    private Task<ToolDispatchResult> HandleActionsCatalogAsync(DynamicToolIntent intent, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var action = intent.GetString("action");
        var includeExamples = intent.GetBool("includeExamples", false);
        var catalog = _actionsCatalogService.Catalog(action, includeExamples);
        var payload = new
        {
            kind = "actions.catalog.v1",
            schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow,
            resource = "actions.catalog",
            data = catalog,
            warnings = Array.Empty<string>()
        };

        var extras = new ToolDispatchExtras(
            Source: new EnvelopeSourceV1
            {
                System = "registry",
                Name = "ActionsCatalogRegistry",
                Cache = "hit",
                Note = "In-memory actions registry"
            },
            Evidence: new[]
            {
                new EnvelopeEvidenceItemV1
                {
                    Id = "ev_actions_catalog",
                    Type = "list",
                    Title = "Danh sách actions & schema",
                    Payload = new { action = action ?? string.Empty, includeExamples, catalog }
                }
            });

        return Task.FromResult(CreateResult(intent, ToolExecutionResult.CreateSuccess("actions.catalog executed", payload), extras));
    }

    private static (Dictionary<string, string?> FiltersApplied, string[] Rejected) CanonicalizeFilters(string resource, IReadOnlyDictionary<string, string?> incoming)
    {
        if (!FilterCatalogRegistry.TryGet(resource, out var catalog))
            return (new Dictionary<string, string?>(incoming, StringComparer.OrdinalIgnoreCase), Array.Empty<string>());

        var aliasToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in catalog.SupportedFilters)
        {
            aliasToKey[f.Key] = f.Key;
            foreach (var a in f.Aliases ?? Array.Empty<string>())
                aliasToKey[a] = f.Key;
        }

        var applied = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var rejected = new List<string>();

        foreach (var (k, v) in incoming)
        {
            if (string.IsNullOrWhiteSpace(k))
                continue;

            if (!aliasToKey.TryGetValue(k.Trim(), out var canonicalKey))
            {
                rejected.Add(k);
                continue;
            }

            applied[canonicalKey] = v?.Trim();
        }

        return (applied, rejected.ToArray());
    }

    private static ToolDispatchResult CreateResult(object normalizedIntent, ToolExecutionResult result, ToolDispatchExtras? extras = null) =>
        new(normalizedIntent, result, extras ?? ToolDispatchExtras.Empty);
}

public sealed record ToolDispatchResult(object NormalizedIntent, ToolExecutionResult Result, ToolDispatchExtras Extras);
