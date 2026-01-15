using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Data;

namespace TILSOFTAI.Infrastructure.Repositories;

/// <summary>
/// SQL Server implementation of dbo.TILSOFTAI_TableKindSignals.
///
/// NOTE: used ONLY when RS0 schema is missing/unreadable.
/// If the table is missing/empty, returns a small built-in default set.
/// </summary>
public sealed class TableKindSignalsRepository : ITableKindSignalsRepository
{
    private readonly SqlServerDbContext _dbContext;
    private readonly ILogger<TableKindSignalsRepository> _logger;

    public TableKindSignalsRepository(SqlServerDbContext dbContext, ILogger<TableKindSignalsRepository>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger ?? NullLogger<TableKindSignalsRepository>.Instance;
    }

    public async Task<IReadOnlyList<TableKindSignalRow>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        var exists = await TableExistsAsync(conn, "dbo", "TILSOFTAI_TableKindSignals", cancellationToken);
        if (!exists)
        {
            _logger.LogDebug("TableKindSignalsRepository: dbo.TILSOFTAI_TableKindSignals not found; using built-in defaults");
            return BuiltInDefaults();
        }

        var list = new List<TableKindSignalRow>(64);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 10;
            cmd.CommandText = @"
                SELECT TableKind, Pattern, Weight, IsRegex, Priority
                FROM dbo.TILSOFTAI_TableKindSignals WITH (NOLOCK)
                WHERE IsEnabled = 1
                ORDER BY Priority ASC, TableKind ASC;";

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var tableKind = reader.IsDBNull(0) ? null : reader.GetString(0);
                var pattern = reader.IsDBNull(1) ? null : reader.GetString(1);
                if (string.IsNullOrWhiteSpace(tableKind) || string.IsNullOrWhiteSpace(pattern))
                    continue;

                var weight = reader.IsDBNull(2) ? 1 : reader.GetInt32(2);
                var isRegex = !reader.IsDBNull(3) && reader.GetBoolean(3);
                var priority = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                list.Add(new TableKindSignalRow(tableKind.Trim(), pattern.Trim(), weight, isRegex, priority));
            }
        }

        if (list.Count == 0)
            return BuiltInDefaults();

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

    private static IReadOnlyList<TableKindSignalRow> BuiltInDefaults() => new[]
    {
        // Summary detection signals.
        new TableKindSignalRow("summary", "^totalcount$", Weight: 3, IsRegex: true, Priority: 0),
        new TableKindSignalRow("summary", "^rowcount$", Weight: 2, IsRegex: true, Priority: 0),
        new TableKindSignalRow("summary", "^count$", Weight: 2, IsRegex: true, Priority: 0),
        new TableKindSignalRow("summary", "^page$", Weight: 2, IsRegex: true, Priority: 0),
        new TableKindSignalRow("summary", "^size$", Weight: 2, IsRegex: true, Priority: 0),
        new TableKindSignalRow("summary", "^isdatasetmode$", Weight: 2, IsRegex: true, Priority: 0),
        new TableKindSignalRow("summary", "^enginerows$", Weight: 2, IsRegex: true, Priority: 0),
        new TableKindSignalRow("summary", "^displayrows$", Weight: 2, IsRegex: true, Priority: 0),
        new TableKindSignalRow("summary", "filter$", Weight: 1, IsRegex: true, Priority: 5),
    };
}
