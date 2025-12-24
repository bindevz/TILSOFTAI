using System.Security;
using System.Text.Json;

namespace TILSOFTAI.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public ExceptionHandlingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = MapException(exception);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var payload = new
        {
            error = new
            {
                message,
                type = exception.GetType().Name,
                code = statusCode.ToString()
            }
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload, _serializerOptions));
    }

    private static (int statusCode, string message) MapException(Exception ex)
    {
        switch (ex)
        {
            case ArgumentException:
            case InvalidOperationException:
            case KeyNotFoundException:
                return (StatusCodes.Status400BadRequest, TrimMessage(ex.Message));
            case SecurityException:
                return (StatusCodes.Status403Forbidden, TrimMessage(ex.Message));
            default:
                return (StatusCodes.Status500InternalServerError, "Internal server error.");
        }
    }

    private static string TrimMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Bad request.";
        }

        var trimmed = message.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }
}
