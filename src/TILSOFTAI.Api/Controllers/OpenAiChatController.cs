using Microsoft.AspNetCore.Mvc;
using TILSOFTAI.Api.Security;
using TILSOFTAI.Orchestration.Chat;
using static System.Net.WebRequestMethods;

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

    private static TILSOFTAI.Domain.ValueObjects.ExecutionContext BuildExecutionContext(HttpContext httpContext)
    {
        var tenantId = httpContext.Request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeader) && !string.IsNullOrWhiteSpace(tenantHeader)
            ? tenantHeader.ToString()
            : "default";

        var userId = httpContext.Request.Headers.TryGetValue("X-User-Id", out var userHeader) && !string.IsNullOrWhiteSpace(userHeader)
            ? userHeader.ToString()
            : "anonymous";

        // 1) Try roles from X-Roles first (nếu bạn vẫn muốn hỗ trợ)
        var rolesHeader = httpContext.Request.Headers["X-Roles"].ToString();
        var roles = rolesHeader
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // 2) Nếu không có roles -> try từ JWT
        if (roles.Count == 0)
        {
            var auth = httpContext.Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = auth.Substring("Bearer ".Length).Trim();
                roles = JwtRoleExtractor.TryExtractRoles(token);
            }
        }

        // 3) Nếu vẫn rỗng -> default viewer (read-only) hoặc deny tuỳ policy
        if (roles.Count == 0)
            roles.Add("user"); // hoặc để rỗng và deny

        //var rolesArray = rolesHeader.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var correlationId = httpContext.Items.TryGetValue("X-Correlation-Id", out var correlation) && correlation is string correlationValue
            ? correlationValue
            : httpContext.TraceIdentifier;

        return new TILSOFTAI.Domain.ValueObjects.ExecutionContext
        {
            TenantId = tenantId,
            UserId = userId,
            Roles = roles,
            CorrelationId = correlationId
        };
    }

    //private TILSOFTAI.Domain.ValueObjects.ExecutionContext BuildExecutionContext(HttpContext http)
    //{
    //    var userId = http.Request.Headers["X-User-Id"].ToString();
    //    var tenantId = http.Request.Headers["X-Tenant-Id"].ToString();

    //    // 1) Try roles from X-Roles first (nếu bạn vẫn muốn hỗ trợ)
    //    var rolesHeader = http.Request.Headers["X-Roles"].ToString();
    //    var roles = rolesHeader
    //        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    //        .ToList();

    //    // 2) Nếu không có roles -> try từ JWT
    //    if (roles.Count == 0)
    //    {
    //        var auth = http.Request.Headers.Authorization.ToString();
    //        if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    //        {
    //            var token = auth.Substring("Bearer ".Length).Trim();
    //            roles = JwtRoleExtractor.TryExtractRoles(token);
    //        }
    //    }

    //    // 3) Nếu vẫn rỗng -> default viewer (read-only) hoặc deny tuỳ policy
    //    if (roles.Count == 0)
    //        roles.Add("viewer"); // hoặc để rỗng và deny

    //    return new TILSOFTAI.Domain.ValueObjects.ExecutionContext(
    //        TenantId: string.IsNullOrWhiteSpace(tenantId) ? "default" : tenantId,
    //        userId: string.IsNullOrWhiteSpace(userId) ? "webui" : userId,
    //        roles: roles.ToArray()
    //    );
    //}
}
