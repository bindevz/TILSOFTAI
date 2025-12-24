namespace TILSOFTAI.Infrastructure.Data;

public sealed class ConfirmationPlanEntity
{
    public string Id { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string DataJson { get; set; } = string.Empty;
}
