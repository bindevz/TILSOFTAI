using System.Globalization;
using System.Text.Json;
using TILSOFTAI.Application.Validators;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Application.Services;

public sealed class ModelsService
{
    private readonly IModelRepository _modelRepository;
    private readonly ConfirmationPlanService _planService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditLogger _auditLogger;

    public ModelsService(IModelRepository modelRepository, ConfirmationPlanService planService, IUnitOfWork unitOfWork, IAuditLogger auditLogger)
    {
        _modelRepository = modelRepository;
        _planService = planService;
        _unitOfWork = unitOfWork;
        _auditLogger = auditLogger;
    }

    public Task<PagedResult<Model>> SearchAsync(string tenantId, string? rangeName, string? modelCode, string? modelName, string? season, string? collection, int page, int size, TSExecutionContext context, CancellationToken cancellationToken)
    {
        BusinessValidators.ValidatePage(page, size);
        season = NormalizeSeason(season);
        return _modelRepository.SearchAsync(context.TenantId, rangeName, modelCode, modelName, season, collection, page, size, cancellationToken);
    }

    public Task<Model?> GetAsync(Guid id, TSExecutionContext context, CancellationToken cancellationToken)
    {
        return _modelRepository.GetAsync(context.TenantId, id, cancellationToken);
    }

    public Task<IReadOnlyCollection<ModelAttribute>> ListAttributesAsync(Guid modelId, TSExecutionContext context, CancellationToken cancellationToken)
    {
        return _modelRepository.ListAttributesAsync(context.TenantId, modelId, cancellationToken);
    }

    public Task<PriceAnalysis> AnalyzePriceAsync(Guid modelId, TSExecutionContext context, CancellationToken cancellationToken)
    {
        return _modelRepository.AnalyzePriceAsync(context.TenantId, modelId, cancellationToken);
    }

    public async Task<object> PrepareCreateAsync(string name, string category, decimal basePrice, IReadOnlyDictionary<string, string> attributes, TSExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Name and category are required.");
        }

        var normalizedAttributes = attributes
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(kvp => NormalizeAttributeName(kvp.Key), kvp => kvp.Value.Trim(), StringComparer.OrdinalIgnoreCase);

        var plan = await _planService.CreatePlanAsync(
            "models.create",
            context,
            new Dictionary<string, string>
            {
                ["name"] = name.Trim(),
                ["category"] = category.Trim(),
                ["basePrice"] = basePrice.ToString(CultureInfo.InvariantCulture),
                ["attributes"] = JsonSerializer.Serialize(normalizedAttributes, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            },
            cancellationToken);

        return new
        {
            confirmation_id = plan.Id,
            expires_at = plan.ExpiresAt,
            preview = new
            {
                name = name.Trim(),
                category = category.Trim(),
                basePrice,
                attributes = normalizedAttributes
            }
        };
    }

    public async Task<Model> CommitCreateAsync(string confirmationId, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var plan = await _planService.ConsumePlanAsync(confirmationId, "models.create", context, cancellationToken);
        var name = plan.Data["name"];
        var category = plan.Data["category"];
        var basePrice = decimal.Parse(plan.Data["basePrice"], NumberStyles.Number, CultureInfo.InvariantCulture);
        var attributes = DeserializeAttributes(plan.Data["attributes"]);

        var model = new Model
        {
            //Id = Guid.NewGuid(),
            //TenantId = context.TenantId,
            //Name = name,
            //Category = category,
            //BasePrice = basePrice,
            //CreatedAt = DateTimeOffset.UtcNow,
            //UpdatedAt = DateTimeOffset.UtcNow,
            //Attributes = attributes.Select(kvp => new ModelAttribute
            //{
            //    Id = Guid.NewGuid(),
            //    Name = kvp.Key,
            //    Value = kvp.Value
            //}).ToList()
        };

        await _unitOfWork.ExecuteTransactionalAsync(async ct =>
        {
            await _modelRepository.CreateAsync(model, ct);
        }, cancellationToken);

        await _auditLogger.LogToolExecutionAsync(context, "models.create.commit", new { confirmationId }, new { model.ModelID, model.ModelUD, model.ModelNM }, cancellationToken);
        return model;
    }

    private static string NormalizeAttributeName(string key) => key.Trim().ToUpperInvariant();

    private static Dictionary<string, string> DeserializeAttributes(string data)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(data, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Invalid confirmation payload.");
        }
    }

    private static string? NormalizeSeason(string? season)
    {
        if (string.IsNullOrWhiteSpace(season)) return null;

        season = season.Trim();

        // 1) Full form: 2024/2025 or 2024-2025 -> keep canonical "2024/2025"
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                season,
                @"\b(20\d{2})\s*[/\-]\s*(20\d{2})\b");

            if (m.Success)
            {
                var y1 = int.Parse(m.Groups[1].Value);
                var y2 = int.Parse(m.Groups[2].Value);

                // optional: enforce contiguous season
                // if (y2 != y1 + 1) ... (either correct or keep as-is)

                return $"{y1}/{y2}";
            }
        }

        // 2) Short form: 24/25 or 24-25 -> expand to 2024/2025
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                season,
                @"\b(\d{2})\s*[/\-]\s*(\d{2})\b");

            if (m.Success)
            {
                var a = int.Parse(m.Groups[1].Value); // 24
                var b = int.Parse(m.Groups[2].Value); // 25

                // Rule: map 00-99 -> 2000-2099 by default
                var y1 = 2000 + a;

                // Prefer contiguous season (y2 = y1 + 1). If user typed 24/26, you can decide policy:
                // - strict: force y2 = y1 + 1
                // - permissive: respect provided b (2000 + b) but adjust if wraparound
                // I'll implement "prefer contiguous" when b == (a+1)%100, else respect b.
                int y2;
                if (b == ((a + 1) % 100))
                {
                    y2 = y1 + 1;
                }
                else
                {
                    // respect b, handle wrap-around (e.g., 99/00 => 2099/2100 if you ever need)
                    // in 2000s-only assumption, 99/00 is unlikely; you can still support it:
                    var baseCentury = 2000;
                    y2 = baseCentury + b;
                    if (b < a) y2 += 100; // 99/00 => 2099/2100
                }

                return $"{y1}/{y2}";
            }
        }

        // 3) If cannot parse, return as-is (or null if you prefer)
        return season;
    }

}
