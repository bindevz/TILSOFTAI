using Microsoft.AspNetCore.Mvc;
using TILSOFTAI.Api.Security;
using TILSOFTAI.Orchestration.Chat;

namespace TILSOFTAI.Api.Controllers;

[ApiController]
[Route("v1/chat/completions")]
public sealed class OpenAiChatController : ControllerBase
{
    private readonly ChatPipeline _chatPipeline;

    public OpenAiChatController(ChatPipeline chatPipeline)
    {
        _chatPipeline = chatPipeline;
    }

    [HttpPost]
    [Produces("application/json")]
    public async Task<ActionResult<ChatCompletionResponse>> Post([FromBody] ChatCompletionRequest request, CancellationToken cancellationToken)
    {
        var context = BuildExecutionContext(HttpContext);

        var hasUser = request.Messages?.Any(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(m.Content)) == true;
        if (!hasUser)
        {
            return BadRequest("User message required.");
        }

        var response = await _chatPipeline.HandleAsync(request, context, cancellationToken);
        return Ok(response);
    }

    private static TILSOFTAI.Domain.ValueObjects.TSExecutionContext BuildExecutionContext(HttpContext httpContext)
    {
        var tenantId = httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader) && !string.IsNullOrWhiteSpace(tenantHeader)
            ? tenantHeader.ToString()
            : "default";

        var userId = httpContext.Request.Headers.TryGetValue("X-User-Id", out var userHeader) && !string.IsNullOrWhiteSpace(userHeader)
            ? userHeader.ToString()
            : "anonymous";

        var rolesHeader = httpContext.Request.Headers["X-Roles"].ToString();
        var roles = rolesHeader
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (roles.Count == 0)
        {
            var auth = httpContext.Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = auth.Substring("Bearer ".Length).Trim();
                roles = JwtRoleExtractor.TryExtractRoles(token);
            }
        }

        if (roles.Count == 0)
            roles.Add("user");

        var correlationId = httpContext.Items.TryGetValue("X-Correlation-Id", out var correlation) && correlation is string correlationValue
            ? correlationValue
            : httpContext.TraceIdentifier;

        var conversationId = httpContext.Items.TryGetValue("X-Conversation-Id", out var conv) && conv is string convValue && !string.IsNullOrWhiteSpace(convValue)
            ? convValue
            : (httpContext.Request.Headers.TryGetValue("X-Conversation-Id", out var convHeader) && !string.IsNullOrWhiteSpace(convHeader)
                ? convHeader.ToString()
                : Guid.NewGuid().ToString("N"));

        // Enterprise telemetry fields
        var requestId = httpContext.TraceIdentifier;
        var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? correlationId;

        return new TILSOFTAI.Domain.ValueObjects.TSExecutionContext
        {
            TenantId = tenantId,
            UserId = userId,
            Roles = roles,
            CorrelationId = correlationId,
            ConversationId = conversationId,
            RequestId = requestId,
            TraceId = traceId
        };
    }

}
