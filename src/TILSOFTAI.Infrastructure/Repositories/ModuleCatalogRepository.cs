using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using System.Text;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Data;

namespace TILSOFTAI.Infrastructure.Repositories;

/// <summary>
/// SQL Server implementation of module routing catalog (dbo.TILSOFTAI_ModuleCatalog).
///
/// If the table is missing/empty, falls back to deriving a conservative signal list
/// from dbo.TILSOFTAI_SPCatalog (aggregated under the "analytics" module) to avoid
/// module-selection dead-ends.
/// </summary>
public sealed class ModuleCatalogRepository : IModuleCatalogRepository
{
    private readonly SqlServerDbContext _dbContext;
    private readonly ILogger<ModuleCatalogRepository> _logger;

    public ModuleCatalogRepository(SqlServerDbContext dbContext, ILogger<ModuleCatalogRepository>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger ?? NullLogger<ModuleCatalogRepository>.Instance;
    }

    public async Task<IReadOnlyList<ModuleSignalRow>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        var exists = await TableExistsAsync(conn, "dbo", "TILSOFTAI_ModuleCatalog", cancellationToken);
        if (!exists)
        {
            _logger.LogWarning("ModuleCatalogRepository: dbo.TILSOFTAI_ModuleCatalog not found; deriving signals from dbo.TILSOFTAI_SPCatalog");
            return await DeriveFromSpCatalogAsync(conn, cancellationToken);
        }

        var list = new List<ModuleSignalRow>(32);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 10;
            cmd.CommandText = @"
                SELECT ModuleName, Signals, Priority
                FROM dbo.TILSOFTAI_ModuleCatalog WITH (NOLOCK)
                WHERE IsEnabled = 1
                ORDER BY Priority DESC, ModuleName ASC;";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var module = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(module))
                    continue;

                var signals = reader.IsDBNull(1) ? null : reader.GetString(1);
                var priority = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                list.Add(new ModuleSignalRow(module.Trim(), signals, priority));
            }
        }

        // If table exists but empty, still derive a conservative fallback.
        if (list.Count == 0)
        {
            _logger.LogWarning("ModuleCatalogRepository: dbo.TILSOFTAI_ModuleCatalog is empty; deriving signals from dbo.TILSOFTAI_SPCatalog");
            return await DeriveFromSpCatalogAsync(conn, cancellationToken);
        }

        return list;
    }

    private static async Task<bool> TableExistsAsync(SqlConnection conn, string schema, string table, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = 10;
        cmd.CommandText = "SELECT OBJECT_ID(@name, 'U');";
        cmd.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 256) { Value = $"{schema}.{table}" });
        var obj = await cmd.ExecuteScalarAsync(ct);
        return obj is not null && obj != DBNull.Value;
    }

    private static async Task<IReadOnlyList<ModuleSignalRow>> DeriveFromSpCatalogAsync(SqlConnection conn, CancellationToken ct)
    {
        // Conservative fallback: aggregate tokens from the governed SP catalog under the analytics module.
        // This avoids hard-coded routing yet keeps the system usable when ModuleCatalog isn't deployed.
        var sb = new StringBuilder();
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 10;
            cmd.CommandText = @"
                SELECT TOP (500)
                    ISNULL(Domain, ''),
                    ISNULL(Entity, ''),
                    ISNULL(Tags, ''),
                    ISNULL(IntentVi, ''),
                    ISNULL(IntentEn, '')
                FROM dbo.TILSOFTAI_SPCatalog WITH (NOLOCK)
                WHERE IsEnabled = 1 AND IsReadOnly = 1 AND IsAtomicCompatible = 1
                ORDER BY UpdatedAtUtc DESC;";

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                for (var i = 0; i < 5; i++)
                {
                    if (reader.IsDBNull(i)) continue;
                    var s = reader.GetString(i);
                    if (!string.IsNullOrWhiteSpace(s))
                        AddTokens(tokens, s);
                }
            }
        }

        // Always include the module name itself.
        tokens.Add("analytics");
        tokens.Add("report");
        tokens.Add("báo cáo");
        tokens.Add("phân tích");

        foreach (var t in tokens.OrderBy(x => x))
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(t);
        }

        return new[] { new ModuleSignalRow("analytics", sb.ToString(), Priority: 0) };
    }

    private static void AddTokens(HashSet<string> target, string text)
    {
        // Split on common separators.
        var parts = text
            .Replace(',', ' ')
            .Replace(';', ' ')
            .Replace('|', ' ')
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Replace('\t', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var p in parts)
        {
            var tok = p.Trim();
            if (tok.Length < 3) continue;
            target.Add(tok);
        }
    }
}
