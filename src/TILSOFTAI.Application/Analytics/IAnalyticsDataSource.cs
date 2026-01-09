using Microsoft.Data.Analysis;
using System.Text.Json;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Application.Analytics;

/// <summary>
/// Provides bounded raw datasets for the Atomic Data Engine.
///
/// This abstraction isolates domain-specific fetching (EF/Repositories, Views, etc.) from
/// the analytics engine. AnalyticsService can remain generic: it resolves a data source by
/// name, requests a dataset, and then runs in-memory analysis.
/// </summary>
public interface IAnalyticsDataSource
{
    /// <summary>The logical name used by tools (e.g., "models", "orders").</summary>
    string SourceName { get; }

    /// <summary>
    /// Fetches a bounded in-memory dataset.
    /// Implementations must enforce tenant scoping and respect the bounds.
    /// </summary>
    Task<DataFrame> FetchAsync(
        IReadOnlyDictionary<string, string?> filters,
        JsonElement? select,
        int maxRows,
        int maxColumns,
        TSExecutionContext context,
        CancellationToken cancellationToken);
}
