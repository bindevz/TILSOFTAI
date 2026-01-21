namespace TILSOFTAI.Infrastructure.Caching;

public sealed class AnalyticsDatasetStoreOptions
{
    public string Provider { get; set; } = "memory";
    public int TtlMinutes { get; set; } = 60;
    public string? RedisConnection { get; set; }
}
