using System.Text.Json.Nodes;
using TILSOFTAI.Application.ContextPackaging;
using TILSOFTAI.Application.Runs;
using TILSOFTAI.Application.Security;
using TILSOFTAI.Contracts.Configuration;
using TILSOFTAI.Contracts.Tools;
using TILSOFTAI.UnitTests;

UnitTestAssert.Equal("MODEL-001", AiRunOrchestrator.ExtractProjectCode("Did MODEL-001 achieve its run target?"));

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

Console.WriteLine("Unit tests passed.");
