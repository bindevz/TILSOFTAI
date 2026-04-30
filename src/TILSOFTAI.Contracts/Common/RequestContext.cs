namespace TILSOFTAI.Contracts.Common;

public sealed record RequestContext(
    Guid TenantId,
    Guid UserId,
    string CorrelationId,
    string? Locale,
    DateTimeOffset RequestTimeUtc);

