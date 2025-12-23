namespace TILSOFTAI.Domain.ValueObjects;

public sealed class ExecutionContext
{
    public required string UserId { get; init; }
    public required string TenantId { get; init; }
    public IReadOnlyCollection<string> Roles { get; init; } = Array.Empty<string>();
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");

    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
