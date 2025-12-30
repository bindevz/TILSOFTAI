using Microsoft.AspNetCore.Mvc;

namespace TILSOFTAI.Api.Controllers;

[ApiController]
[Route("v1/models")]
public sealed class OpenAiModelsController : ControllerBase
{
    [HttpGet]
    [Produces("application/json")]
    public IActionResult Get()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var response = new
        {
            @object = "list",
            data = new object[]
            {
                new
                {
                    id = "TILSOFT-AI",
                    @object = "model",
                    created = now,
                    owned_by = "tilsoftai",
                    permission = Array.Empty<object>(),
                    root = "TILSOFT-AI",
                    parent = (string?)null
                }
            }
        };

        return Ok(response);
    }
}
