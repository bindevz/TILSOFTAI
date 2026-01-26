using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

public interface IEntityGraphRepository
{
    Task<IReadOnlyList<EntityGraphSearchHit>> SearchAsync(string query, int topK, CancellationToken cancellationToken);
    
    Task<EntityGraphDefinition?> GetByCodeAsync(string graphCode, CancellationToken cancellationToken);
}
