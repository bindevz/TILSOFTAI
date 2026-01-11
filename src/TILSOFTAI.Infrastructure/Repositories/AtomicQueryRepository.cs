using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Data;
using TILSOFTAI.Infrastructure.Data.Tabular;

namespace TILSOFTAI.Infrastructure.Repositories;

/// <summary>
/// Generic executor for stored procedures returning the standardized AtomicQuery output:
/// RS0 schema, RS1 summary, RS2..N tables.
/// </summary>
public sealed class AtomicQueryRepository : IAtomicQueryRepository
{
    private readonly SqlServerDbContext _dbContext;

    public AtomicQueryRepository(SqlServerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AtomicQueryResult> ExecuteAsync(
        string storedProcedure,
        IReadOnlyDictionary<string, object?> parameters,
        AtomicQueryReadOptions readOptions,
        CancellationToken cancellationToken)
    {
        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = storedProcedure;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = 60;

        if (parameters is not null)
        {
            foreach (var kv in parameters)
            {
                var name = kv.Key;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!name.StartsWith("@", StringComparison.Ordinal))
                    name = "@" + name;

                var value = kv.Value ?? DBNull.Value;
                cmd.Parameters.Add(new SqlParameter(name, value));
            }
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        // Enforce hard bounds while materializing.
        var options = new SqlAtomicQueryReader.ReadOptions(
            MaxRowsPerTable: Math.Clamp(readOptions.MaxRowsPerTable, 1, 200_000),
            MaxRowsSummary: Math.Clamp(readOptions.MaxRowsSummary, 0, 50_000),
            MaxSchemaRows: Math.Clamp(readOptions.MaxSchemaRows, 1, 500_000));

        var atomic = await SqlAtomicQueryReader.ReadAsync(reader, options, cancellationToken);

        // Optional: clamp number of tables (fail-closed).
        if (readOptions.MaxTables > 0 && atomic.Tables.Count > readOptions.MaxTables)
        {
            var trimmed = atomic.Tables.Take(readOptions.MaxTables).ToArray();
            atomic = atomic with { Tables = trimmed };
        }

        return atomic;
    }
}
