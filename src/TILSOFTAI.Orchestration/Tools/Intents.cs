using TILSOFTAI.Domain.Entities;

namespace TILSOFTAI.Orchestration.Tools;

// Dynamic filter intents (Phase 1+): ...
public sealed record DynamicFiltersIntent(IReadOnlyDictionary<string, string?> Filters);

public sealed record PagedDynamicFiltersIntent(IReadOnlyDictionary<string, string?> Filters, int Page, int PageSize);

public sealed record OrderQueryIntent(IReadOnlyDictionary<string, string?> Filters, int PageNumber, int PageSize);

public sealed record OrderSummaryIntent(IReadOnlyDictionary<string, string?> Filters);

public sealed record UpdateEmailIntent(Guid? CustomerId, string? Email, string? ConfirmationId);

public sealed record ModelsCountIntent(IReadOnlyDictionary<string, string?> Filters);

public sealed record ModelsSearchIntent(IReadOnlyDictionary<string, string?> Filters, int Page, int PageSize);

public sealed record ModelGetIntent(Guid ModelId);
public sealed record ModelListAttributesIntent(Guid ModelId);
public sealed record ModelPriceAnalyzeIntent(Guid ModelId);
public sealed record ModelCreatePrepareIntent(string Name, string Category, decimal BasePrice, IReadOnlyDictionary<string, string> Attributes);
public sealed record ModelCreateCommitIntent(string ConfirmationId);

public sealed record CustomerSearchIntent(IReadOnlyDictionary<string, string?> Filters, int Page, int PageSize);

public sealed record OrderCreatePrepareIntent(Guid CustomerId, Guid ModelId, string? Color, int Quantity);
public sealed record OrderCreateCommitIntent(string ConfirmationId);

public sealed record FiltersCatalogIntent(string? Resource, bool IncludeValues);
