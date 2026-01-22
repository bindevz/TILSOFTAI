using System.Text.Json.Serialization;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Application.Analytics;

public sealed record AnalyticsDatasetDto(
    [property: JsonPropertyName("datasetId")] string DatasetId,
    [property: JsonPropertyName("schemaDigest")] object? SchemaDigest,
    [property: JsonPropertyName("tabularData")] TabularData TabularData,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("createdAtUtc")] DateTimeOffset CreatedAtUtc);
