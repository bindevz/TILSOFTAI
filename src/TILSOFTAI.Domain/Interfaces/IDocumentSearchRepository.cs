using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Domain.Interfaces;

public interface IDocumentSearchRepository
{
    Task<IReadOnlyList<DocumentChunkHit>> SearchByVectorAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken);
}
