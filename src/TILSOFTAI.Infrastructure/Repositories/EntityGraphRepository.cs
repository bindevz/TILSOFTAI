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

namespace TILSOFTAI.Infrastructure.Repositories;

public sealed class EntityGraphRepository : IEntityGraphRepository
{
    private readonly SqlServerDbContext _dbContext;
    private readonly SqlSettings _sqlSettings;
    private readonly EntityGraphSettings _entityGraphSettings;
    private readonly ILogger<EntityGraphRepository> _logger;

    public EntityGraphRepository(
        SqlServerDbContext dbContext,
        IOptions<AppSettings> appSettings,
        ILogger<EntityGraphRepository>? logger = null)
    {
        _dbContext = dbContext;
        _sqlSettings = appSettings.Value.Sql;
        _entityGraphSettings = appSettings.Value.EntityGraph;
        _logger = logger ?? NullLogger<EntityGraphRepository>.Instance;
    }

    public async Task<IReadOnlyList<EntityGraphSearchHit>> SearchAsync(
        string query,
        int topK,
        CancellationToken cancellationToken)
    {
        var hits = new List<EntityGraphSearchHit>();

        try
        {
            var connString = _dbContext.Database.GetConnectionString();
            await using var conn = new SqlConnection(connString);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = _entityGraphSettings.SearchSpName;
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandTimeout = _sqlSettings.CommandTimeoutSeconds;

            cmd.Parameters.Add(new SqlParameter("@Query", SqlDbType.NVarChar, 256) { Value = query });
            cmd.Parameters.Add(new SqlParameter("@TopK", SqlDbType.Int) { Value = topK });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            // RS1: graphs
            while (await reader.ReadAsync(cancellationToken))
            {
                hits.Add(new EntityGraphSearchHit(
                    GraphId: reader.GetInt32(reader.GetOrdinal("GraphId")),
                    GraphCode: reader.GetString(reader.GetOrdinal("GraphCode")),
                    Domain: GetString(reader, "Domain"),
                    Entity: GetString(reader, "Entity"),
                    Tags: GetString(reader, "Tags"),
                    RootSpName: GetString(reader, "RootSpName"),
                    DescriptionVi: GetString(reader, "DescriptionVi"),
                    DescriptionEn: GetString(reader, "DescriptionEn"),
                    Score: reader.GetInt32(reader.GetOrdinal("Score")),
                    UpdatedAtUtc: GetDateTimeOffset(reader, "UpdatedAtUtc") ?? DateTimeOffset.UtcNow,
                    Packs: Array.Empty<EntityGraphPackHint>()
                ));
            }

            // RS2: packs for selected graphs (optional)
            var packMap = new Dictionary<int, List<EntityGraphPackHint>>();
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    var graphId = reader.GetInt32(reader.GetOrdinal("GraphId"));

                    if (!packMap.TryGetValue(graphId, out var list))
                    {
                        list = new List<EntityGraphPackHint>();
                        packMap[graphId] = list;
                    }

                    list.Add(new EntityGraphPackHint(
                        GraphId: graphId,
                        PackCode: reader.GetString(reader.GetOrdinal("PackCode")),
                        PackType: reader.GetString(reader.GetOrdinal("PackType")),
                        SpName: reader.GetString(reader.GetOrdinal("SpName")),
                        Tags: GetString(reader, "Tags"),
                        SortOrder: reader.GetInt32(reader.GetOrdinal("SortOrder"))
                    ));
                }
            }

            if (packMap.Count == 0)
                return hits;

            return hits.Select(h =>
            {
                if (packMap.TryGetValue(h.GraphId, out var packs))
                    return h with { Packs = packs.OrderBy(p => p.SortOrder).ToList() };
                return h;
            }).ToList();
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error searching entity graph catalog: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<EntityGraphDefinition?> GetByCodeAsync(
        string graphCode,
        CancellationToken cancellationToken)
    {
        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _entityGraphSettings.GetSpName;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = _sqlSettings.CommandTimeoutSeconds;

        cmd.Parameters.Add(new SqlParameter("@GraphCode", SqlDbType.NVarChar, 128) { Value = graphCode });

        EntityGraphSummary? summary = null;
        var packs = new List<EntityGraphPackSummary>();
        var nodes = new List<EntityGraphNode>();
        var edges = new List<EntityGraphEdge>();
        var glossary = new List<EntityGraphGlossaryEntry>();

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            // 1. Graph Catalog (1 row)
            if (await reader.ReadAsync(cancellationToken))
            {
                summary = new EntityGraphSummary(
                    GraphId: reader.GetInt32(reader.GetOrdinal("GraphId")),
                    GraphCode: reader.GetString(reader.GetOrdinal("GraphCode")),
                    Domain: GetString(reader, "Domain"),
                    Entity: GetString(reader, "Entity"),
                    Tags: GetString(reader, "Tags"),
                    RootSpName: GetString(reader, "RootSpName"),
                    DescriptionVi: GetString(reader, "DescriptionVi"),
                    DescriptionEn: GetString(reader, "DescriptionEn"),
                    UpdatedAtUtc: GetDateTimeOffset(reader, "UpdatedAtUtc") ?? DateTimeOffset.UtcNow,
                    CreatedAtUtc: GetDateTimeOffset(reader, "CreatedAtUtc") ?? DateTimeOffset.UtcNow
                );
            }

            if (summary == null) return null;

            // 2. Packs (N rows)
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    packs.Add(new EntityGraphPackSummary(
                        PackId: reader.GetInt32(reader.GetOrdinal("PackId")),
                        GraphId: reader.GetInt32(reader.GetOrdinal("GraphId")),
                        PackCode: reader.GetString(reader.GetOrdinal("PackCode")),
                        PackType: reader.GetString(reader.GetOrdinal("PackType")),
                        SpName: reader.GetString(reader.GetOrdinal("SpName")),
                        Tags: GetString(reader, "Tags"),
                        IntentVi: GetString(reader, "IntentVi"),
                        IntentEn: GetString(reader, "IntentEn"),
                        ParamsJson: GetString(reader, "ParamsJson"),
                        ExampleJson: GetString(reader, "ExampleJson"),
                        ProducesDatasetsJson: GetString(reader, "ProducesDatasetsJson"),
                        SortOrder: reader.GetInt32(reader.GetOrdinal("SortOrder"))
                    ));
                }
            }

            // 3. Nodes (N rows)
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    nodes.Add(new EntityGraphNode(
                        NodeId: reader.GetInt32(reader.GetOrdinal("NodeId")),
                        GraphId: reader.GetInt32(reader.GetOrdinal("GraphId")),
                        DatasetName: reader.GetString(reader.GetOrdinal("DatasetName")),
                        TableKind: GetString(reader, "TableKind"),
                        Delivery: GetString(reader, "Delivery"),
                        PrimaryKeyJson: GetString(reader, "PrimaryKeyJson"),
                        IdColumnsJson: GetString(reader, "IdColumnsJson"),
                        DimensionHintsJson: GetString(reader, "DimensionHintsJson"),
                        MeasureHintsJson: GetString(reader, "MeasureHintsJson"),
                        TimeColumnsJson: GetString(reader, "TimeColumnsJson"),
                        Notes: GetString(reader, "Notes")
                    ));
                }
            }

            // 4. Edges (N rows)
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    edges.Add(new EntityGraphEdge(
                        EdgeId: reader.GetInt32(reader.GetOrdinal("EdgeId")),
                        GraphId: reader.GetInt32(reader.GetOrdinal("GraphId")),
                        LeftDataset: reader.GetString(reader.GetOrdinal("LeftDataset")),
                        RightDataset: reader.GetString(reader.GetOrdinal("RightDataset")),
                        LeftKeysJson: reader.GetString(reader.GetOrdinal("LeftKeysJson")),
                        RightKeysJson: reader.GetString(reader.GetOrdinal("RightKeysJson")),
                        How: reader.GetString(reader.GetOrdinal("How")),
                        RightPrefix: GetString(reader, "RightPrefix"),
                        SelectRightJson: GetString(reader, "SelectRightJson"),
                        Notes: GetString(reader, "Notes")
                    ));
                }
            }

            // 5. Glossary (N rows)
            if (await reader.NextResultAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    glossary.Add(new EntityGraphGlossaryEntry(
                        GlossaryId: reader.GetInt32(reader.GetOrdinal("GlossaryId")),
                        GraphId: reader.GetInt32(reader.GetOrdinal("GraphId")),
                        Lang: reader.GetString(reader.GetOrdinal("Lang")),
                        Term: reader.GetString(reader.GetOrdinal("Term")),
                        Canonical: reader.GetString(reader.GetOrdinal("Canonical")),
                        Notes: GetString(reader, "Notes")
                    ));
                }
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error getting entity graph definition for {GraphCode}: {Message}", graphCode, ex.Message);
            throw;
        }

        return new EntityGraphDefinition(summary, packs, nodes, edges, glossary);
    }

    private static string? GetString(SqlDataReader reader, string name)
    {
        var i = reader.GetOrdinal(name);
        if (reader.IsDBNull(i)) return null;
        return reader.GetString(i);
    }

    private static DateTimeOffset? GetDateTimeOffset(SqlDataReader reader, string name)
    {
        var i = reader.GetOrdinal(name);
        if (reader.IsDBNull(i)) return null;
        var dt = reader.GetDateTime(i);
        return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc));
    }
}
