using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TSExecutionContext = TILSOFTAI.Domain.ValueObjects.TSExecutionContext;

namespace TILSOFTAI.Application.Services;

public sealed class ConfirmationPlanService
{
    private readonly IConfirmationPlanStore _store;
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(10);

    public ConfirmationPlanService(IConfirmationPlanStore store)
    {
        _store = store;
    }

    public async Task<ConfirmationPlan> CreatePlanAsync(string tool, TSExecutionContext context, IReadOnlyDictionary<string, string> data, CancellationToken cancellationToken)
    {
        var plan = new ConfirmationPlan
        {
            Id = Guid.NewGuid().ToString("N"),
            Tool = tool,
            TenantId = context.TenantId,
            UserId = context.UserId,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_ttl),
            Data = data
        };

        await _store.SaveAsync(plan, cancellationToken);
        return plan;
    }

    public async Task<ConfirmationPlan> ConsumePlanAsync(string confirmationId, string tool, TSExecutionContext context, CancellationToken cancellationToken)
    {
        var plan = await _store.GetAsync(confirmationId, cancellationToken);
        if (plan is null)
        {
            throw new InvalidOperationException("Confirmation not found.");
        }

        if (!string.Equals(plan.Tool, tool, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Confirmation tool mismatch.");
        }

        if (!string.Equals(plan.TenantId, context.TenantId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(plan.UserId, context.UserId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Confirmation context mismatch.");
        }

        if (plan.IsExpired(DateTimeOffset.UtcNow))
        {
            await _store.RemoveAsync(plan.Id, cancellationToken);
            throw new InvalidOperationException("Confirmation expired.");
        }

        await _store.RemoveAsync(plan.Id, cancellationToken);
        return plan;
    }
}
