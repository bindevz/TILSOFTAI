using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace TILSOFTAI.Api.Middleware;

public sealed class RateLimitOptions
{
    public int RequestsPerMinute { get; set; } = 120;
    public int BlockDurationSeconds { get; set; } = 30;
}

public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private static readonly ConcurrentDictionary<string, RateLimitState> States = new();

    public RateLimitMiddleware(RequestDelegate next, IOptions<RateLimitOptions> options, ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var now = DateTimeOffset.UtcNow;
        var state = States.GetOrAdd(key, _ => new RateLimitState());

        if (state.IsBlocked(now))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("Rate limit exceeded.");
            return;
        }

        state.Prune(now, TimeSpan.FromMinutes(1));
        if (state.Requests.Count >= _options.RequestsPerMinute)
        {
            state.BlockUntil = now.AddSeconds(_options.BlockDurationSeconds);
            _logger.LogWarning("Rate limit triggered for {Key}", key);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("Rate limit exceeded.");
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
