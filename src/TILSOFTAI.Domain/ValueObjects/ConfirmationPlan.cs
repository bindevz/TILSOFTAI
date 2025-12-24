namespace TILSOFTAI.Domain.ValueObjects;

public sealed record ConfirmationPlan
{
    public required string Id { get; init; }
    public required string Tool { get; init; }
    public required string TenantId { get; init; }
    public required string UserId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required IReadOnlyDictionary<string, string> Data { get; init; }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
