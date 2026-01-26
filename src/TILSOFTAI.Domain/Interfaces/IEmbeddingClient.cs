namespace TILSOFTAI.Domain.Interfaces;

public interface IEmbeddingClient
{
    /// <summary>
    /// Creates an embedding vector for the given input text.
    /// </summary>
    Task<float[]> CreateEmbeddingAsync(string input, CancellationToken cancellationToken);
}
