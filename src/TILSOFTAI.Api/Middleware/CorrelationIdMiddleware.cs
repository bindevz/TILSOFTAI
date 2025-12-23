using System.Diagnostics;

namespace TILSOFTAI.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var correlationId) || string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            context.Request.Headers[HeaderName] = correlationId;
        }

        context.Response.Headers[HeaderName] = correlationId!;
        context.Items[HeaderName] = correlationId!;

        await _next(context);
    }
}
