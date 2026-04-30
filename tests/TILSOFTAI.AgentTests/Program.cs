using TILSOFTAI.Agent.AgentOrchestrator;
using TILSOFTAI.Application.Capabilities;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.AgentTests;

RequestContext context = new(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000101"), Guid.NewGuid().ToString("D"), "en", DateTimeOffset.UtcNow);
ControlledAgentWorkflow workflow = new(new InMemoryCapabilitySearchService());

var verify = await workflow.PlanAsync(context, "Did MODEL-001 achieve its run target?", "Model", CancellationToken.None);
AgentTestAssert.Equal("model.project.run.verify", verify.Capability.CapabilityCode);
AgentTestAssert.Equal("Model.GetProjectRunVerification", verify.Capability.Tool.ToolName);
AgentTestAssert.Equal("MODEL-001", verify.Parameters["projectCode"]!.GetValue<string>());

var failed = await workflow.PlanAsync(context, "What failed in the latest run of MODEL-001?", "Model", CancellationToken.None);
AgentTestAssert.Equal("model.project.run.failed_checks", failed.Capability.CapabilityCode);
AgentTestAssert.Equal("Model.GetFailedRunChecks", failed.Capability.Tool.ToolName);

var latest = await workflow.PlanAsync(context, "Summarize latest run status for MODEL-002", "Model", CancellationToken.None);
AgentTestAssert.Equal("model.project.run.latest", latest.Capability.CapabilityCode);
AgentTestAssert.Equal("Model.GetLatestProjectRun", latest.Capability.Tool.ToolName);

Console.WriteLine("Agent behavior tests passed.");
