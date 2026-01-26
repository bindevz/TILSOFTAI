using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TILSOFTAI.Configuration;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Data;

namespace TILSOFTAI.Infrastructure.Repositories;

public sealed class DocumentSearchRepository : IDocumentSearchRepository
{
    private readonly SqlServerDbContext _dbContext;
    private readonly SqlSettings _sql;
    private readonly DocumentSearchSettings _doc;
    private readonly ILogger<DocumentSearchRepository> _logger;

    public DocumentSearchRepository(
        SqlServerDbContext dbContext,
        IOptions<AppSettings> appSettings,
        ILogger<DocumentSearchRepository>? logger = null)
    {
        _dbContext = dbContext;
        _sql = appSettings.Value.Sql;
        _doc = appSettings.Value.DocumentSearch;
        _logger = logger ?? NullLogger<DocumentSearchRepository>.Instance;
    }

    public async Task<IReadOnlyList<DocumentChunkHit>> SearchByVectorAsync(
        float[] queryVector,
        int topK,
        CancellationToken cancellationToken)
    {
        if (!_doc.Enabled) return Array.Empty<DocumentChunkHit>();
        if (queryVector is null || queryVector.Length == 0) return Array.Empty<DocumentChunkHit>();

        topK = topK < 1 ? 1 : topK;
        if (_doc.MaxTopK > 0) topK = Math.Min(topK, _doc.MaxTopK);

        var hits = new List<DocumentChunkHit>();
        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = _doc.SearchSpName;
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.CommandTimeout = _sql.CommandTimeoutSeconds;

        var vecJson = VectorToJson(queryVector);
        cmd.Parameters.Add(new SqlParameter("@QueryEmbeddingJson", SqlDbType.NVarChar, -1) { Value = vecJson });
        cmd.Parameters.Add(new SqlParameter("@TopK", SqlDbType.Int) { Value = topK });

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                hits.Add(new DocumentChunkHit(
                    DocId: reader.GetInt32(reader.GetOrdinal("DocId")),
                    ChunkId: reader.GetInt64(reader.GetOrdinal("ChunkId")),
                    ChunkNo: reader.GetInt32(reader.GetOrdinal("ChunkNo")),
                    Title: GetString(reader, "Title"),
                    Uri: GetString(reader, "Uri"),
                    Snippet: GetString(reader, "Snippet") ?? string.Empty,
                    Distance: reader.GetDouble(reader.GetOrdinal("Distance"))
                ));
            }
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Error running document vector search: {Message}", ex.Message);
            throw;
        }

        return hits;
    }

    private static string VectorToJson(float[] vector)
    {
        // Fast JSON array serialization, invariant culture.
        // Example: [0.1,0.2,...]
        var sb = new System.Text.StringBuilder(vector.Length * 8);
        sb.Append('[');
        for (var i = 0; i < vector.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(vector[i].ToString("G9", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static string? GetString(SqlDataReader reader, string name)
    {
        var i = reader.GetOrdinal(name);
        if (reader.IsDBNull(i)) return null;
        return reader.GetString(i);
    }
}
