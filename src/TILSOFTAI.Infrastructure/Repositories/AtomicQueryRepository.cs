using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using System.Data;
using TILSOFTAI.Configuration;
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
    private readonly ILogger<AtomicQueryRepository> _logger;
    private readonly ITableKindSignalsRepository? _tableKindSignalsRepository;
    private readonly IAppCache? _cache;
    private readonly SqlSettings _sql;

    public AtomicQueryRepository(
        SqlServerDbContext dbContext,
        ILogger<AtomicQueryRepository>? logger = null,
        ITableKindSignalsRepository? tableKindSignalsRepository = null,
        IAppCache? cache = null,
        IOptions<AppSettings>? settings = null)
    {
        _dbContext = dbContext;
        _logger = logger ?? NullLogger<AtomicQueryRepository>.Instance;
        _tableKindSignalsRepository = tableKindSignalsRepository;
        _cache = cache;
        _sql = (settings?.Value ?? new AppSettings()).Sql;
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
        var timeoutSeconds = _sql.CommandTimeoutSeconds <= 0 ? 60 : _sql.CommandTimeoutSeconds;
        cmd.CommandTimeout = Math.Clamp(timeoutSeconds, 5, 1800);

        _logger.LogInformation("AtomicQueryRepository.Execute start sp={Sp} paramCount={ParamCount}", storedProcedure, parameters?.Count ?? 0);

        var droppedParams = new List<string>();


        HashSet<string>? actualParamNames = null;
        try
        {
            actualParamNames = await TryGetStoredProcedureParamNamesAsync(conn, storedProcedure, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AtomicQueryRepository.Execute metadata lookup failed sp={Sp}; will accept provided params", storedProcedure);

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
                    droppedParams.Add(name);
                    continue;
                }


                var value = kv.Value ?? DBNull.Value;
                cmd.Parameters.Add(new SqlParameter(name, value));
            }
        }

        _logger.LogInformation("AtomicQueryRepository.Execute prepared sp={Sp} actualParamCount={ActualParamCount} providedParamCount={ProvidedParamCount} droppedParamCount={DroppedParamCount}",
            storedProcedure,
            actualParamNames?.Count ?? -1,
            parameters?.Count ?? 0,
            droppedParams.Count);
        if (droppedParams.Count > 0)
        {
            _logger.LogDebug("AtomicQueryRepository.Execute dropped params sp={Sp} dropped={Dropped}", storedProcedure, string.Join(",", droppedParams.Take(20)));
            _logger.LogWarning("AtomicQueryRepository.Execute dropped unknown params sp={Sp} dropped={Dropped}", storedProcedure, string.Join(",", droppedParams.Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);


        // Optional: load SQL-configured table-kind signals used ONLY for RS0-missing fallback.
        IReadOnlyList<TableKindSignalRow>? tableKindSignals = null;
        if (_tableKindSignalsRepository is not null)
        {
            var key = $"atomic:tableKindSignals:v1:{conn.DataSource}:{conn.Database}".ToLowerInvariant();
            if (_cache is not null)
            {
                tableKindSignals = await _cache.GetOrAddAsync(
                    key,
                    () => _tableKindSignalsRepository.GetEnabledAsync(cancellationToken),
                    ttl: TimeSpan.FromMinutes(10));
            }
            else
            {
                tableKindSignals = await _tableKindSignalsRepository.GetEnabledAsync(cancellationToken);
            }
        }

        // Enforce hard bounds while materializing.
        var options = new SqlAtomicQueryReader.ReadOptions(
            MaxRowsPerTable: Math.Clamp(readOptions.MaxRowsPerTable, 1, 200_000),
            MaxRowsSummary: Math.Clamp(readOptions.MaxRowsSummary, 0, 50_000),
            MaxSchemaRows: Math.Clamp(readOptions.MaxSchemaRows, 1, 500_000),
            TableKindSignals: tableKindSignals);

        var atomic = await SqlAtomicQueryReader.ReadAsync(reader, options, cancellationToken);

        _logger.LogInformation("AtomicQueryRepository.Execute read done sp={Sp} hasSummary={HasSummary} tables={Tables}", storedProcedure, atomic.Summary is not null, atomic.Tables.Count);


        // Optional: clamp number of tables (fail-closed).
        if (readOptions.MaxTables > 0 && atomic.Tables.Count > readOptions.MaxTables)
        {
            var trimmed = atomic.Tables.Take(readOptions.MaxTables).ToArray();
            atomic = atomic with { Tables = trimmed };
        }

        return atomic;
    }
}
