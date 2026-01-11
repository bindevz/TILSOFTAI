using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

    public AtomicCatalogRepository(SqlServerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<AtomicCatalogSearchHit>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "dbo.TILSOFTAI_sp_catalog_search";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = 30;

        cmd.Parameters.Add(new SqlParameter("@q", SqlDbType.NVarChar, 4000) { Value = query });
        cmd.Parameters.Add(new SqlParameter("@top", SqlDbType.Int) { Value = topK });

        var list = new List<AtomicCatalogSearchHit>();

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var hit = new AtomicCatalogSearchHit(
                SpName: GetString(reader, "SpName") ?? string.Empty,
                Domain: GetString(reader, "Domain"),
                Entity: GetString(reader, "Entity"),
                IntentVi: GetString(reader, "IntentVi") ?? string.Empty,
                IntentEn: GetString(reader, "IntentEn"),
                Tags: GetString(reader, "Tags"),
                Score: GetInt(reader, "Score") ?? 0,
                ParamsJson: GetString(reader, "ParamsJson"),
                ExampleJson: GetString(reader, "ExampleJson"));

            if (!string.IsNullOrWhiteSpace(hit.SpName))
                list.Add(hit);
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
