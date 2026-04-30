using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Common;

namespace TILSOFTAI.Api.Middleware;

public sealed class RequestContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, IRequestContextAccessor accessor)
    {
        if (IsHealthRequest(httpContext.Request.Path))
        {
            await next(httpContext);
            return;
        }

        if (!TryReadGuid(httpContext, "X-Tenant-Id", out Guid tenantId))
            return;

        if (!TryReadGuid(httpContext, "X-User-Id", out Guid userId))
            return;

        string correlationId = httpContext.Request.Headers.TryGetValue("X-Correlation-Id", out var values) && !string.IsNullOrWhiteSpace(values.ToString())
            ? values.ToString()
            : Guid.NewGuid().ToString("D");

        accessor.Current = new RequestContext(tenantId, userId, correlationId, httpContext.Request.Headers.AcceptLanguage.ToString(), DateTimeOffset.UtcNow);
        httpContext.Response.Headers["X-Correlation-Id"] = correlationId;
        using IDisposable? scope = httpContext.RequestServices.GetRequiredService<ILogger<RequestContextMiddleware>>()
            .BeginScope(new Dictionary<string, object?>
            {
                ["TenantId"] = tenantId,
                ["UserId"] = userId,
                ["CorrelationId"] = correlationId
            });

        await next(httpContext);
    }

    private static bool IsHealthRequest(PathString path) =>
        path.StartsWithSegments("/health/live") || path.StartsWithSegments("/health/ready");

    private static bool TryReadGuid(HttpContext httpContext, string headerName, out Guid value)
    {
        value = Guid.Empty;
        if (!httpContext.Request.Headers.TryGetValue(headerName, out var headerValues) || string.IsNullOrWhiteSpace(headerValues.ToString()))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return false;
        }

        if (!Guid.TryParse(headerValues.ToString(), out value))
        {
            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            return false;
        }

        return true;
    }
}

