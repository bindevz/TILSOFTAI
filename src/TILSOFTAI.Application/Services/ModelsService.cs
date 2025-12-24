using System.Globalization;
using System.Text.Json;
using TILSOFTAI.Application.Validators;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using ExecutionContext = TILSOFTAI.Domain.ValueObjects.ExecutionContext;

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

    public Task<PagedResult<ProductModel>> SearchAsync(string? category, string? name, int page, int size, ExecutionContext context, CancellationToken cancellationToken)
    {
        BusinessValidators.ValidatePage(page, size);
        return _modelRepository.SearchAsync(context.TenantId, category, name, page, size, cancellationToken);
    }

    public Task<ProductModel?> GetAsync(Guid id, ExecutionContext context, CancellationToken cancellationToken)
    {
        return _modelRepository.GetAsync(context.TenantId, id, cancellationToken);
    }

    public Task<IReadOnlyCollection<ProductModelAttribute>> ListAttributesAsync(Guid modelId, ExecutionContext context, CancellationToken cancellationToken)
    {
        return _modelRepository.ListAttributesAsync(context.TenantId, modelId, cancellationToken);
    }

    public Task<PriceAnalysis> AnalyzePriceAsync(Guid modelId, ExecutionContext context, CancellationToken cancellationToken)
    {
        return _modelRepository.AnalyzePriceAsync(context.TenantId, modelId, cancellationToken);
    }

    public async Task<object> PrepareCreateAsync(string name, string category, decimal basePrice, IReadOnlyDictionary<string, string> attributes, ExecutionContext context, CancellationToken cancellationToken)
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

    public async Task<ProductModel> CommitCreateAsync(string confirmationId, ExecutionContext context, CancellationToken cancellationToken)
    {
        var plan = await _planService.ConsumePlanAsync(confirmationId, "models.create", context, cancellationToken);
        var name = plan.Data["name"];
        var category = plan.Data["category"];
        var basePrice = decimal.Parse(plan.Data["basePrice"], NumberStyles.Number, CultureInfo.InvariantCulture);
        var attributes = DeserializeAttributes(plan.Data["attributes"]);

        var model = new ProductModel
        {
            Id = Guid.NewGuid(),
            TenantId = context.TenantId,
            Name = name,
            Category = category,
            BasePrice = basePrice,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Attributes = attributes.Select(kvp => new ProductModelAttribute
            {
                Id = Guid.NewGuid(),
                Name = kvp.Key,
                Value = kvp.Value
            }).ToList()
        };

        await _unitOfWork.ExecuteTransactionalAsync(async ct =>
        {
            await _modelRepository.CreateAsync(model, ct);
        }, cancellationToken);

        await _auditLogger.LogToolExecutionAsync(context, "models.create.commit", new { confirmationId }, new { model.Id, model.Name, model.Category }, cancellationToken);
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
}
