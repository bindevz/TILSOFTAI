namespace TILSOFTAI.Domain.Interfaces;

/// <summary>
/// Stores short-lived analytics datasets server-side so LLM does not need to carry raw data.
///
/// Note: The dataset is stored as an opaque object to avoid introducing analytics implementation
/// details (e.g., DataFrame) into the Domain layer.
/// </summary>
public interface IAnalyticsDatasetStore
{
    Task StoreAsync(string datasetId, object dataset, TimeSpan ttl, CancellationToken cancellationToken);
    bool TryGet(string datasetId, out object dataset);
    void Remove(string datasetId);
}
