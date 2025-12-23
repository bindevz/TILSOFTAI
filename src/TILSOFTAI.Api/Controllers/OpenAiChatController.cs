using Microsoft.AspNetCore.Mvc;
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
        var response = await _chatPipeline.HandleAsync(request, context, cancellationToken);
        return Ok(response);
    }

    private static TILSOFTAI.Domain.ValueObjects.ExecutionContext BuildExecutionContext(HttpContext httpContext)
    {
        var tenantId = httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader) && !string.IsNullOrWhiteSpace(tenantHeader)
            ? tenantHeader.ToString()
            : "default";

        var userId = httpContext.Request.Headers.TryGetValue("X-User-Id", out var userHeader) && !string.IsNullOrWhiteSpace(userHeader)
            ? userHeader.ToString()
            : "anonymous";

        var rolesHeader = httpContext.Request.Headers.TryGetValue("X-Roles", out var roles)
            ? roles.ToString()
            : string.Empty;

        var rolesArray = rolesHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var correlationId = httpContext.Items.TryGetValue("X-Correlation-Id", out var correlation) && correlation is string correlationValue
            ? correlationValue
            : httpContext.TraceIdentifier;

        return new TILSOFTAI.Domain.ValueObjects.ExecutionContext
        {
            TenantId = tenantId,
            UserId = userId,
            Roles = rolesArray,
            CorrelationId = correlationId
        };
    }
}
