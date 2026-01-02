using TILSOFTAI.Domain.Entities;

namespace TILSOFTAI.Orchestration.Tools;

public sealed record OrderQueryIntent(
    Guid? CustomerId,
    OrderStatus? Status,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    int PageNumber,
    int PageSize,
    string? Season,
    string? Metric);

public sealed record OrderSummaryIntent(
    Guid? CustomerId,
    OrderStatus? Status,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    string? Season,
    string? Metric);

public sealed record UpdateEmailIntent(Guid? CustomerId, string? Email, string? ConfirmationId);
public sealed record ModelSearchIntent(string? RangeName, string? ModelCode, string? ModelName, string? Season, string? Collection, int Page, int PageSize);
public sealed record ModelGetIntent(Guid ModelId);
public sealed record ModelListAttributesIntent(Guid ModelId);
public sealed record ModelPriceAnalyzeIntent(Guid ModelId);
public sealed record ModelCreatePrepareIntent(string Name, string Category, decimal BasePrice, IReadOnlyDictionary<string, string> Attributes);
public sealed record ModelCreateCommitIntent(string ConfirmationId);
public sealed record CustomerSearchIntent(string Query, int Page, int PageSize);
public sealed record OrderCreatePrepareIntent(Guid CustomerId, Guid ModelId, string? Color, int Quantity);
public sealed record OrderCreateCommitIntent(string ConfirmationId);
public sealed record ModelsFiltersCatalogIntent();
public sealed record FiltersCatalogIntent(string? Resource, bool IncludeValues);


