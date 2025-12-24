using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

public interface IConfirmationPlanStore
{
    Task SaveAsync(ConfirmationPlan plan, CancellationToken cancellationToken);
    Task<ConfirmationPlan?> GetAsync(string id, CancellationToken cancellationToken);
    Task RemoveAsync(string id, CancellationToken cancellationToken);
}
