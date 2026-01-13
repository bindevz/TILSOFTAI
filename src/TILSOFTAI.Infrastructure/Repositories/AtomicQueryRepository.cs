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

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTimeOffset fetchedAtUtc, HashSet<string> names)> _paramNameCache
            = new(StringComparer.OrdinalIgnoreCase);

        private static async Task<HashSet<string>?> TryGetStoredProcedureParamNamesAsync(
            SqlConnection conn,
            string storedProcedure,
            CancellationToken cancellationToken)
        {
            // Cache per database + procedure to avoid repeated sys lookups.
            var key = $"{conn.DataSource}|{conn.Database}|{storedProcedure}".ToLowerInvariant();

            if (_paramNameCache.TryGetValue(key, out var cached) &&
                (DateTimeOffset.UtcNow - cached.fetchedAtUtc) < TimeSpan.FromMinutes(10))
            {
                return cached.names;
            }

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
SELECT p.name
FROM sys.parameters p
WHERE p.object_id = OBJECT_ID(@spName)
  AND p.is_output = 0
  AND p.name <> '@RETURN_VALUE';";
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 10;
            cmd.Parameters.Add(new SqlParameter("@spName", SqlDbType.NVarChar, 256) { Value = storedProcedure });

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                {
                    var name = reader.GetString(0);
                    if (!string.IsNullOrWhiteSpace(name))
                        set.Add(name.Trim());
                }
            }

            if (set.Count == 0)
                return null;

            _paramNameCache[key] = (DateTimeOffset.UtcNow, set);
            return set;
        }

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

        HashSet<string>? actualParamNames = null;
        try
        {
            actualParamNames = await TryGetStoredProcedureParamNamesAsync(conn, storedProcedure, cancellationToken);
        }
        catch
        {
            // Best-effort only. If sys metadata is unavailable (permissions), we fall back to trusting input.
        }

        if (parameters is not null)
        {
            foreach (var kv in parameters)
            {
                var name = kv.Key;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!name.StartsWith("@", StringComparison.Ordinal))
                    name = "@" + name;

                if (actualParamNames is not null && actualParamNames.Count > 0 && !actualParamNames.Contains(name))
                {
                    // Unknown parameter for this stored procedure; ignore to avoid SQL errors.
                    continue;
                }


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
