using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using TILSOFTAI.Agent.AgentFramework;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Application.ContextPackaging;
using TILSOFTAI.Application.Runs;
using TILSOFTAI.Application.Security;
using TILSOFTAI.Contracts.Api;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Contracts.Configuration;
using TILSOFTAI.Contracts.Tools;
using TILSOFTAI.Infrastructure.DependencyInjection;
using TILSOFTAI.Infrastructure.LocalAi;
using TILSOFTAI.Persistence.DependencyInjection;
using TILSOFTAI.UnitTests;

ModelParameterBinder binder = new();
UnitTestAssert.Equal("MODEL-001", binder.BindProjectCode("Did MODEL-001 achieve its run target?").ProjectCode);
UnitTestAssert.Equal(false, binder.BindProjectCode("Did the project achieve its run target?").Success);
UnitTestAssert.Equal("InvalidProjectCode", binder.BindProjectCode("Did MODEL-XYZ pass?").ErrorCode);

TilsoftAiOptions invalid = new();
UnitTestAssert.True(ConfigurationValidator.Validate(invalid).Count > 0, "Invalid configuration must fail validation.");
UnitTestAssert.Equal("ab****yz", ConfigurationValidator.Redact("ab1234yz"));

ToolExecutionResult result = new(
    "Model.GetProjectRunVerification",
    "model.project.run.verify",
    [
        new Dictionary<string, object?> { ["ProjectCode"] = "MODEL-001", ["Metric"] = "Run Status", ["Value"] = "Passed", ["IsSensitive"] = false },
        new Dictionary<string, object?> { ["ProjectCode"] = "MODEL-001", ["Metric"] = "Email", ["Value"] = "person@example.local", ["IsSensitive"] = true }
    ],
    ["ProjectCode = MODEL-001"],
    TimeSpan.FromMilliseconds(2));

var sanitized = SanitizerAndContextPackager.Sanitize(result);
UnitTestAssert.Equal(1, sanitized.Count);
JsonObject package = SanitizerAndContextPackager.Build("Did MODEL-001 achieve its run target?", result, Guid.NewGuid());
UnitTestAssert.True(SanitizerAndContextPackager.EstimateTokens(package) < 1000, "Context package should be compact.");

FinalAnswerProvenanceValidator validator = new();
Guid artifactId = Guid.NewGuid();
FinalAnswer untrusted = new("summary", [], [], [], [new AnswerProvenance("Fake.Tool", [], Guid.NewGuid())], []);
FinalAnswer corrected = validator.ValidateAndAttachSystemProvenance(untrusted, result, artifactId);
UnitTestAssert.Equal("Model.GetProjectRunVerification", corrected.Provenance[0].ToolName);
UnitTestAssert.Equal(artifactId, corrected.Provenance[0].ArtifactId);

ServiceCollection productionServices = new();
TilsoftAiOptions validOptions = new()
{
    ConnectionStrings = new ConnectionStringOptions { TilsoftAi = "Server=(local);Database=TILSOFTAI;Integrated Security=True;TrustServerCertificate=True" },
    Ai = new AiOptions { OpenAICompatible = new OpenAICompatibleOptions { BaseUrl = "http://localhost:6688/v1/", ApiKey = "test", ChatModel = "chat", EmbeddingModel = "embed" } },
    Artifacts = new ArtifactOptions { RootPath = Path.Combine(Path.GetTempPath(), "tilsoftai-unit-artifacts") }
};
productionServices.AddSingleton(validOptions);
productionServices.AddScoped<IRequestContextAccessor, RequestContextAccessor>();
productionServices.AddSingleton<FinalAnswerProvenanceValidator>();
productionServices.AddTilsoftAiPersistence();
productionServices.AddTilsoftAiInfrastructure();
productionServices.AddTilsoftAiAgentFramework();
using ServiceProvider provider = productionServices.BuildServiceProvider();
UnitTestAssert.True(!productionServices.Any(d => d.ImplementationType?.Name.StartsWith("Testing", StringComparison.Ordinal) == true), "Production DI must not register testing implementations.");
using IServiceScope scope1 = provider.CreateScope();
using IServiceScope scope2 = provider.CreateScope();
RequestContext scopedContext = new(Guid.NewGuid(), Guid.NewGuid(), "corr", "en", DateTimeOffset.UtcNow);
scope1.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current = scopedContext;
UnitTestAssert.Equal(scopedContext, scope1.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current);
UnitTestAssert.Equal<RequestContext?>(null, scope2.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current);

FinalAnswer answer = new("ok", [], ["insight"], ["caveat"], [new AnswerProvenance("Model.GetProjectRunVerification", ["ProjectCode = MODEL-001"], artifactId)], ["follow"]);
string content = JsonSerializer.Serialize(answer, new JsonSerializerOptions(JsonSerializerDefaults.Web));
string aiEnvelope = JsonSerializer.Serialize(new { choices = new[] { new { message = new { role = "assistant", content } } } });
OpenAICompatibleLocalAiClient aiClient = new(new HttpClient(new StubHttpHandler(aiEnvelope)) { BaseAddress = new Uri("http://localhost:6688/v1/") }, validOptions);
AiChatResponse chatResponse = await aiClient.ChatAsync(new AiChatRequest("system", "user", package), CancellationToken.None);
UnitTestAssert.Equal("ok", chatResponse.Answer.Summary);

OpenAICompatibleLocalAiClient invalidAiClient = new(new HttpClient(new StubHttpHandler("{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":\"not-json\"}}]}")) { BaseAddress = new Uri("http://localhost:6688/v1/") }, validOptions);
await UnitTestAssert.ThrowsAsync<InvalidOperationException>(() => invalidAiClient.ChatAsync(new AiChatRequest("system", "user", package), CancellationToken.None));

Console.WriteLine("Unit tests passed.");

file sealed class StubHttpHandler(string responseBody) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(response);
    }
}
