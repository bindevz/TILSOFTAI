using System.Security;
using System.Text.Json;
using TILSOFTAI.Api.Localization;

namespace TILSOFTAI.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IApiTextLocalizer _localizer;
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public ExceptionHandlingMiddleware(RequestDelegate next, IApiTextLocalizer localizer)
    {
        _next = next;
        _localizer = localizer;
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

    private (int statusCode, string message) MapException(Exception ex)
    {
        switch (ex)
        {
            case TaskCanceledException:
            case TimeoutException:
                return (StatusCodes.Status504GatewayTimeout, _localizer.Get(ApiTextKeys.Error_GatewayTimeout));
            case HttpRequestException:
                return (StatusCodes.Status502BadGateway, _localizer.Get(ApiTextKeys.Error_BadGateway));
            case ArgumentException:
            case InvalidOperationException:
            case KeyNotFoundException:
                return (StatusCodes.Status400BadRequest, NormalizeOrDefault(ex.Message, ApiTextKeys.Error_BadRequest));
            case SecurityException:
                return (StatusCodes.Status403Forbidden, NormalizeOrDefault(ex.Message, ApiTextKeys.Error_BadRequest));
            default:
                return (StatusCodes.Status500InternalServerError, _localizer.Get(ApiTextKeys.Error_InternalServerError));
        }
    }

    private static string TrimMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        var trimmed = message.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }

    private string NormalizeOrDefault(string message, string defaultKey)
    {
        var trimmed = TrimMessage(message);
        return string.IsNullOrWhiteSpace(trimmed) ? _localizer.Get(defaultKey) : trimmed;
    }
}
