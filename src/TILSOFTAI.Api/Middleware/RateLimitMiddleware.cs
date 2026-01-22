using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TILSOFTAI.Api.Localization;
using TILSOFTAI.Configuration;

namespace TILSOFTAI.Api.Middleware;

public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AppSettings _settings;
    private readonly IApiTextLocalizer _localizer;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private static readonly ConcurrentDictionary<string, RateLimitState> States = new();

    public RateLimitMiddleware(
        RequestDelegate next,
        IOptions<AppSettings> settings,
        IApiTextLocalizer localizer,
        ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _settings = settings.Value;
        _localizer = localizer;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var now = DateTimeOffset.UtcNow;
        var state = States.GetOrAdd(key, _ => new RateLimitState());
        var limits = _settings.Api.RateLimit;

        if (state.IsBlocked(now))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync(_localizer.Get(ApiTextKeys.Error_RateLimitExceeded));
            return;
        }

        state.Prune(now, TimeSpan.FromMinutes(1));
        if (state.Requests.Count >= limits.RequestsPerMinute)
        {
            state.BlockUntil = now.AddSeconds(limits.BlockDurationSeconds);
            _logger.LogWarning("Rate limit triggered for {Key}", key);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync(_localizer.Get(ApiTextKeys.Error_RateLimitExceeded));
            return;
        }

        state.Requests.Enqueue(now);
        await _next(context);
    }

    private sealed class RateLimitState
    {
        public ConcurrentQueue<DateTimeOffset> Requests { get; } = new();
        public DateTimeOffset? BlockUntil { get; set; }

        public bool IsBlocked(DateTimeOffset now) => BlockUntil.HasValue && now < BlockUntil;

        public void Prune(DateTimeOffset now, TimeSpan window)
        {
            while (Requests.TryPeek(out var timestamp))
            {
                if (now - timestamp <= window)
                {
                    break;
                }

                Requests.TryDequeue(out _);
            }
        }
    }
}
