using System.Diagnostics;
using System.Text.Json.Nodes;
using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Contracts.Tools;
using TILSOFTAI.Persistence.Connection;

namespace TILSOFTAI.Persistence.Tools;

public sealed class SqlModelToolRuntime(SqlCommandExecutor executor) : IToolRuntime
{
    public async Task<ToolExecutionResult> ExecuteAsync(RequestContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        if (request.Tool.ToolTypeIsNotStoredProcedure())
            throw new InvalidOperationException("Only SQL stored procedure tools are supported for the Model runtime.");

        if (!request.Tool.SqlProcedureName.StartsWith("model.usp_", StringComparison.Ordinal))
            throw new InvalidOperationException("Model tool procedure must come from trusted model metadata.");

        string? projectCode = request.Parameters["projectCode"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(projectCode))
            throw new ArgumentException("projectCode is required.", nameof(request));

        Stopwatch stopwatch = Stopwatch.StartNew();
        var rows = await executor.QueryRowsAsync(request.Tool.SqlProcedureName,
        [
            SqlParameterFactory.UniqueIdentifier("@TenantId", context.TenantId),
            SqlParameterFactory.UniqueIdentifier("@UserId", context.UserId),
            SqlParameterFactory.NVarChar("@CorrelationId", context.CorrelationId, 100),
            SqlParameterFactory.NVarChar("@ProjectCode", projectCode, 50)
        ], Math.Max(1, request.Tool.TimeoutMs / 1000), request.Tool.MaxRows, cancellationToken);
        stopwatch.Stop();

        return new ToolExecutionResult(request.Tool.ToolName, InferCapabilityCode(request.Tool.ToolName), SqlToolResultMapper.Normalize(rows), [$"ProjectCode = {projectCode}"], stopwatch.Elapsed);
    }

    private static string InferCapabilityCode(string toolName) => toolName switch
    {
        "Model.GetFailedRunChecks" => "model.project.run.failed_checks",
        "Model.GetLatestProjectRun" => "model.project.run.latest",
        _ => "model.project.run.verify"
    };
}

file static class ToolDescriptorExtensions
{
    public static bool ToolTypeIsNotStoredProcedure(this ToolDescriptor tool) =>
        !tool.SqlProcedureName.StartsWith("model.usp_", StringComparison.Ordinal);
}

