using TILSOFTAI.Domain.Entities;

namespace TILSOFTAI.Orchestration.Tools;

public sealed record OrderQueryIntent(Guid? CustomerId, OrderStatus? Status, DateTimeOffset? StartDate, DateTimeOffset? EndDate, int PageNumber, int PageSize);

public sealed record OrderSummaryIntent(Guid? CustomerId, OrderStatus? Status, DateTimeOffset? StartDate, DateTimeOffset? EndDate);

public sealed record UpdateEmailIntent(Guid CustomerId, string Email);
