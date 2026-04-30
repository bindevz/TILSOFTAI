using TILSOFTAI.Application.Abstractions;
using TILSOFTAI.Contracts.Common;
using TILSOFTAI.Contracts.Tools;
using TILSOFTAI.Persistence.Connection;

namespace TILSOFTAI.Persistence.Capabilities;

public sealed class SqlCapabilitySearchService(SqlCommandExecutor executor) : ICapabilitySearchService
{
    public async Task<IReadOnlyList<CapabilityDescriptor>> SearchAsync(RequestContext context, string question, string? domainHint, CancellationToken cancellationToken)
    {
        var rows = await executor.QueryRowsAsync("ai.usp_SearchModelCapabilities",
        [
            SqlParameterFactory.UniqueIdentifier("@TenantId", context.TenantId),
            SqlParameterFactory.UniqueIdentifier("@UserId", context.UserId),
            SqlParameterFactory.NVarChar("@Question", question),
            SqlParameterFactory.NVarChar("@DomainHint", domainHint, 100),
            SqlParameterFactory.Int("@TopK", 5)
        ], 30, 5, cancellationToken);

        return rows.Select(Map).ToList();
    }

    private static CapabilityDescriptor Map(IReadOnlyDictionary<string, object?> row)
    {
        ToolDescriptor tool = new(
            ReadGuid(row, "ToolId"),
            ReadString(row, "ToolName"),
            ReadString(row, "SqlProcedureName"),
            ReadString(row, "InputJsonSchema"),
            ReadInt(row, "MaxRows"),
            ReadInt(row, "TimeoutMs"),
            ReadString(row, "RequiredPermissionCode"));

        return new(
            ReadGuid(row, "CapabilityId"),
            ReadString(row, "ModuleCode"),
            ReadString(row, "CapabilityCode"),
            ReadString(row, "CapabilityName"),
            ReadString(row, "Description"),
            ReadString(row, "RequiredPermissionCode"),
            tool);
    }

    private static string ReadString(IReadOnlyDictionary<string, object?> row, string key) => row.TryGetValue(key, out object? value) ? value?.ToString() ?? string.Empty : string.Empty;
    private static int ReadInt(IReadOnlyDictionary<string, object?> row, string key) => row.TryGetValue(key, out object? value) ? Convert.ToInt32(value) : 0;
    private static Guid ReadGuid(IReadOnlyDictionary<string, object?> row, string key) => Guid.Parse(ReadString(row, key));
}

