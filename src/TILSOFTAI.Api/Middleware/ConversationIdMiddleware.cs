using System.Diagnostics;

namespace TILSOFTAI.Api.Middleware;

/// <summary>
/// Ensures every request has a stable conversation id.
/// - If client does not send X-Conversation-Id, server generates one.
/// - Server echoes X-Conversation-Id back in the response header.
/// - The value is also stored in HttpContext.Items for downstream components.
/// </summary>
public sealed class ConversationIdMiddleware
{
    private const string HeaderName = "X-Conversation-Id";
    private readonly RequestDelegate _next;

    public ConversationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var conversationId) || string.IsNullOrWhiteSpace(conversationId))
        {
            // Prefer TraceId when available to keep correlation-friendly ids, otherwise generate.
            conversationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            context.Request.Headers[HeaderName] = conversationId;
        }

        context.Response.Headers[HeaderName] = conversationId!;
        context.Items[HeaderName] = conversationId!;

        await _next(context);
    }
}
