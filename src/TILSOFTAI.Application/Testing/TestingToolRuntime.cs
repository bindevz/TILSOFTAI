using System.Diagnostics;
using System.Text.Json.Nodes;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Contracts.Tools;

namespace TILSOFTAI.Application.Testing;

public sealed class TestingToolRuntime : IToolRuntime
{
    public Task<ToolExecutionResult> ExecuteAsync(RequestContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        if (context.UserId.ToString("D").EndsWith("102", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Testing user lacks model.project.run.read permission.");

        string? projectCode = request.Parameters["projectCode"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(projectCode) || !projectCode.StartsWith("MODEL-", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("projectCode is required and must identify a Model project.", nameof(request));

        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows = request.Tool.ToolName switch
        {
            "Model.GetFailedRunChecks" => FailedRows(projectCode),
            "Model.GetLatestProjectRun" => LatestRows(projectCode),
            _ => VerificationRows(projectCode)
        };
        stopwatch.Stop();

        string capability = request.Tool.ToolName switch
        {
            "Model.GetFailedRunChecks" => "model.project.run.failed_checks",
            "Model.GetLatestProjectRun" => "model.project.run.latest",
            _ => "model.project.run.verify"
        };

        return Task.FromResult(new ToolExecutionResult(request.Tool.ToolName, capability, rows, [$"ProjectCode = {projectCode}"], stopwatch.Elapsed));
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> VerificationRows(string projectCode) =>
    [
        Row(projectCode, "Run Status", "Passed", false),
        Row(projectCode, "Overall Score", "96.5", false),
        Row(projectCode, "Failed Checks", "0", false),
        Row(projectCode, "Warning Checks", "1", false),
        Row(projectCode, "InternalReviewerEmail", "masked@example.local", true)
    ];

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> LatestRows(string projectCode) =>
    [
        Row(projectCode, "Latest Run Status", projectCode.EndsWith("002", StringComparison.Ordinal) ? "Warning" : "Passed", false),
        Row(projectCode, "Overall Score", projectCode.EndsWith("002", StringComparison.Ordinal) ? "88.0" : "96.5", false)
    ];

    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> FailedRows(string projectCode) =>
    [
        Row(projectCode, "Failed Checks", projectCode.EndsWith("002", StringComparison.Ordinal) ? "1" : "0", false),
        Row(projectCode, "SensitiveEvidence", "secret-review-note", true)
    ];

    private static Dictionary<string, object?> Row(string projectCode, string metric, string value, bool sensitive) =>
        new()
        {
            ["ProjectCode"] = projectCode,
            ["Metric"] = metric,
            ["Value"] = value,
            ["IsSensitive"] = sensitive
        };
}
