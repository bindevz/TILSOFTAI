namespace TILSOFTAI.Infrastructure.Caching;

public sealed class AnalyticsResultCacheOptions
{
    public string Provider { get; set; } = "memory";
    public int TtlMinutes { get; set; } = 10;
    public string? RedisConnection { get; set; }
}
