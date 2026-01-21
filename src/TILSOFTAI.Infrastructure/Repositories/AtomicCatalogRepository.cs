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
/// SQL Server implementation of the governed stored procedure catalog.
/// </summary>
public sealed class AtomicCatalogRepository : IAtomicCatalogRepository
{
    private readonly SqlServerDbContext _dbContext;
    private readonly ILogger<AtomicCatalogRepository> _logger;

    public AtomicCatalogRepository(SqlServerDbContext dbContext, ILogger<AtomicCatalogRepository>? logger = null)
    {
        _dbContext = dbContext;
        _logger = logger ?? NullLogger<AtomicCatalogRepository>.Instance;
    }


    public async Task<IReadOnlyList<AtomicCatalogSearchHit>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        query = (query ?? string.Empty).Trim();
        topK = Math.Clamp(topK, 1, 25);

        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);
        var hits = new List<AtomicCatalogSearchHit>();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 15;
            cmd.CommandText = @"
SELECT TOP (@topK)
    SpName,
    Domain,
    Entity,
    IntentVi,
    IntentEn,
    Tags,
    ParamsJson,
    ExampleJson,
    SchemaHintsJson,
    UpdatedAtUtc,
    CASE
        WHEN SpName = @query THEN 1000
        WHEN SpName LIKE @like THEN 400
        WHEN Tags LIKE @like THEN 200
        WHEN IntentVi LIKE @like THEN 150
        WHEN IntentEn LIKE @like THEN 150
        WHEN Domain LIKE @like THEN 100
        WHEN Entity LIKE @like THEN 100
        ELSE 1
    END AS Score
FROM dbo.TILSOFTAI_SPCatalog WITH (NOLOCK)
WHERE IsEnabled = 1 AND IsReadOnly = 1 AND IsAtomicCompatible = 1
  AND (SpName LIKE @like OR Tags LIKE @like OR IntentVi LIKE @like OR IntentEn LIKE @like OR Domain LIKE @like OR Entity LIKE @like)
ORDER BY Score DESC, UpdatedAtUtc DESC;";

            cmd.Parameters.Add(new SqlParameter("@topK", SqlDbType.Int) { Value = topK });
            cmd.Parameters.Add(new SqlParameter("@query", SqlDbType.NVarChar, 256) { Value = query });
            cmd.Parameters.Add(new SqlParameter("@like", SqlDbType.NVarChar, 256) { Value = "%" + query + "%" });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                hits.Add(new AtomicCatalogSearchHit(
                    SpName: GetString(reader, "SpName") ?? string.Empty,
                    Domain: GetString(reader, "Domain"),
                    Entity: GetString(reader, "Entity"),
                    IntentVi: GetString(reader, "IntentVi") ?? string.Empty,
                    IntentEn: GetString(reader, "IntentEn"),
                    Tags: GetString(reader, "Tags"),
                    Score: GetInt(reader, "Score") ?? 1,
                    ParamsJson: GetString(reader, "ParamsJson"),
                    ExampleJson: GetString(reader, "ExampleJson"),
                    SchemaHintsJson: GetString(reader, "SchemaHintsJson")));
            }
        }

        _logger.LogInformation("AtomicCatalogRepository.Search hits={Hits} query={Query}", hits.Count, query);

        // If no catalog hits found, fall back to sys.procedures for "did you mean".
        if (hits.Count == 0)
        {
            _logger.LogWarning("AtomicCatalogRepository.Search no catalog hits; falling back to sys.procedures query={Query}", query);
            var sysHits = await SearchSysProceduresAsync(conn, query, topK: Math.Max(topK, 5), cancellationToken);

            if (sysHits.Count == 0)
            {
                sysHits = await SearchSysProceduresAsync(conn, string.Empty, topK: Math.Max(topK, 5), cancellationToken);
            }

            return sysHits;
        }

        return hits;
    }

    private static async Task<IReadOnlyList<AtomicCatalogSearchHit>> SearchSysProceduresAsync(
        SqlConnection conn,
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        var list = new List<AtomicCatalogSearchHit>();

        await using var cmd = conn.CreateCommand();
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = 10;

        // Conservative filter: only scan the known naming convention.
        // This keeps it fast even on large databases.
        var where = new System.Text.StringBuilder();
        where.Append("WHERE p.name LIKE N'TILSOFTAI_sp_%' ");

        if (!string.IsNullOrWhiteSpace(query))
        {
            where.Append("AND p.name LIKE @like ");
        }

        cmd.CommandText = $@"
SELECT TOP (@top)
    s.name AS SchemaName,
    p.name AS ProcName
FROM sys.procedures p
JOIN sys.schemas s ON s.schema_id = p.schema_id
{where}
ORDER BY p.modify_date DESC;";

        cmd.Parameters.Add(new SqlParameter("@top", SqlDbType.Int) { Value = Math.Clamp(topK, 1, 50) });

        if (!string.IsNullOrWhiteSpace(query))
            cmd.Parameters.Add(new SqlParameter("@like", SqlDbType.NVarChar, 128) { Value = "%" + query + "%" });

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.IsDBNull(0) ? "dbo" : reader.GetString(0);
            var name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            if (string.IsNullOrWhiteSpace(name)) continue;

            var sp = $"{schema}.{name}";

            list.Add(new AtomicCatalogSearchHit(
                SpName: sp,
                Domain: null,
                Entity: null,
                IntentVi: "Unregistered stored procedure (from sys.procedures). Add it to dbo.TILSOFTAI_SPCatalog to enable AI execution.",
                IntentEn: "Unregistered stored procedure (from sys.procedures). Add it to dbo.TILSOFTAI_SPCatalog to enable AI execution.",
                Tags: null,
                Score: 1,
                ParamsJson: null,
                ExampleJson: null,
                SchemaHintsJson: null));
        }

        return list;
    }


    public async Task<AtomicCatalogEntry?> GetByNameAsync(
        string storedProcedure,
        CancellationToken cancellationToken)
    {
        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
SELECT TOP (1)
    SpName,
    IsEnabled,
    IsReadOnly,
    IsAtomicCompatible,
    Domain,
    Entity,
    IntentVi,
    IntentEn,
    Tags,
    ParamsJson,
    ExampleJson,
    SchemaHintsJson,
    UpdatedAtUtc
FROM dbo.TILSOFTAI_SPCatalog
WHERE SpName = @spName;";
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = 30;
        cmd.Parameters.Add(new SqlParameter("@spName", SqlDbType.NVarChar, 256) { Value = storedProcedure });

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new AtomicCatalogEntry(
            SpName: GetString(reader, "SpName") ?? storedProcedure,
            IsEnabled: GetBool(reader, "IsEnabled") ?? false,
            IsReadOnly: GetBool(reader, "IsReadOnly") ?? false,
            IsAtomicCompatible: GetBool(reader, "IsAtomicCompatible") ?? false,
            Domain: GetString(reader, "Domain"),
            Entity: GetString(reader, "Entity"),
            IntentVi: GetString(reader, "IntentVi") ?? string.Empty,
            IntentEn: GetString(reader, "IntentEn"),
            Tags: GetString(reader, "Tags"),
            ParamsJson: GetString(reader, "ParamsJson"),
            ExampleJson: GetString(reader, "ExampleJson"),
            SchemaHintsJson: GetString(reader, "SchemaHintsJson"),
            UpdatedAtUtc: GetDateTimeOffset(reader, "UpdatedAtUtc") ?? DateTimeOffset.UtcNow);
    }

    private static string? GetString(SqlDataReader reader, string name)
    {
        var i = TryGetOrdinal(reader, name);
        if (i < 0 || reader.IsDBNull(i)) return null;
        return reader.GetString(i);
    }

    private static int? GetInt(SqlDataReader reader, string name)
    {
        var i = TryGetOrdinal(reader, name);
        if (i < 0 || reader.IsDBNull(i)) return null;
        return reader.GetInt32(i);
    }

    private static bool? GetBool(SqlDataReader reader, string name)
    {
        var i = TryGetOrdinal(reader, name);
        if (i < 0 || reader.IsDBNull(i)) return null;
        return reader.GetBoolean(i);
    }

    private static DateTimeOffset? GetDateTimeOffset(SqlDataReader reader, string name)
    {
        var i = TryGetOrdinal(reader, name);
        if (i < 0 || reader.IsDBNull(i)) return null;

        // SQL Server datetime/datetime2 -> DateTime
        var dt = reader.GetDateTime(i);
        return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
    }

    private static int TryGetOrdinal(SqlDataReader reader, string name)
    {
        try
        {
            return reader.GetOrdinal(name);
        }
        catch
        {
            return -1;
        }
    }
}
