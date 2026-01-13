using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Linq;
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

        var variants = BuildQueryVariants(query);
        var tokens = Tokenize(variants).Take(12).ToArray();
        _logger.LogInformation("AtomicCatalogRepository.Search start query={Query} topK={TopK} variants={Variants} tokens={Tokens}", query, topK, variants.Count, string.Join("|", tokens));

        // 1) Lightweight pre-filter in SQL (avoid scanning the whole catalog).
        // We only reference columns that are known to exist (see GetByNameAsync).
        var candidates = new List<(AtomicCatalogEntry entry, int seedScore)>();

        await using (var cmd = conn.CreateCommand())
        {
            var where = new System.Text.StringBuilder();
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 15;

            where.Append("WHERE IsEnabled = 1 AND IsReadOnly = 1 AND IsAtomicCompatible = 1 ");

            if (tokens.Length > 0)
            {
                where.Append("AND (");
                for (var i = 0; i < tokens.Length; i++)
                {
                    if (i > 0) where.Append(" OR ");
                    where.Append($"SpName LIKE @t{i} OR Tags LIKE @t{i} OR IntentVi LIKE @t{i} OR IntentEn LIKE @t{i} OR Domain LIKE @t{i} OR Entity LIKE @t{i}");
                }
                where.Append(") ");
            }

            cmd.CommandText = $@"
                SELECT TOP (400)
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
                FROM dbo.TILSOFTAI_SPCatalog WITH (NOLOCK)
                {where}
                ORDER BY UpdatedAtUtc DESC;";

            for (var i = 0; i < tokens.Length; i++)
            {
                cmd.Parameters.Add(new SqlParameter($"@t{i}", SqlDbType.NVarChar, 256) { Value = "%" + tokens[i] + "%" });
            }

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var entry = new AtomicCatalogEntry(
                    SpName: GetString(reader, "SpName") ?? string.Empty,
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

                candidates.Add((entry, 0));
            }
        }

        _logger.LogInformation("AtomicCatalogRepository.Search candidates={Candidates}", candidates.Count);

        // 2) If catalog is empty or no candidates found, fall back to sys.procedures for "did you mean".
        // This does NOT bypass governance; it only prevents "empty result" loops.
        if (candidates.Count == 0)
        {
            _logger.LogWarning("AtomicCatalogRepository.Search no catalog candidates; falling back to sys.procedures tokens={Tokens}", string.Join("|", tokens));

            // 2.1) Try token-based lookup first
            var sysHits = await SearchSysProceduresAsync(conn, tokens, topK: Math.Max(topK, 5), cancellationToken);

            // 2.2) Hard fallback: never return empty. If token-based search yields nothing,
            // return a small recent list of procedures under the naming convention.
            if (sysHits.Count == 0)
            {
                sysHits = await SearchSysProceduresAsync(conn, Array.Empty<string>(), topK: Math.Max(topK, 5), cancellationToken);
            }

            return sysHits;
        }

        // 3) Score in-memory (fast, extensible).
        var scored = candidates
            .Select(x => new AtomicCatalogSearchHit(
                SpName: x.entry.SpName,
                Domain: x.entry.Domain,
                Entity: x.entry.Entity,
                IntentVi: x.entry.IntentVi,
                IntentEn: x.entry.IntentEn,
                Tags: x.entry.Tags,
                Score: Score(x.entry, variants, tokens),
                ParamsJson: x.entry.ParamsJson,
                ExampleJson: x.entry.ExampleJson))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.SpName, StringComparer.OrdinalIgnoreCase)
            .Take(topK)
            .ToList();

        if (scored.Count > 0)
        {
            var top = string.Join(", ", scored.Take(Math.Min(scored.Count, 5)).Select(h => $"{h.SpName}:{h.Score}"));
            _logger.LogInformation("AtomicCatalogRepository.Search scored hits={Hits} top={Top}", scored.Count, top);
        }


        // 4) Final fallback: never return empty if the catalog has data.
        if (scored.Count == 0)
        {
            scored = candidates
                .OrderByDescending(x => x.entry.UpdatedAtUtc)
                .Take(topK)
                .Select(x => new AtomicCatalogSearchHit(
                    SpName: x.entry.SpName,
                    Domain: x.entry.Domain,
                    Entity: x.entry.Entity,
                    IntentVi: x.entry.IntentVi,
                    IntentEn: x.entry.IntentEn,
                    Tags: x.entry.Tags,
                    Score: 1,
                    ParamsJson: x.entry.ParamsJson,
                    ExampleJson: x.entry.ExampleJson))
                .ToList();

        if (scored.Count > 0)
        {
            var top = string.Join(", ", scored.Take(Math.Min(scored.Count, 5)).Select(h => $"{h.SpName}:{h.Score}"));
            _logger.LogInformation("AtomicCatalogRepository.Search scored hits={Hits} top={Top}", scored.Count, top);
        }

        }

        return scored;
    }

    private static async Task<IReadOnlyList<AtomicCatalogSearchHit>> SearchSysProceduresAsync(
        SqlConnection conn,
        string[] tokens,
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

        if (tokens.Length > 0)
        {
            where.Append("AND (");
            for (var i = 0; i < tokens.Length; i++)
            {
                if (i > 0) where.Append(" OR ");
                where.Append($"p.name LIKE @t{i}");
            }
            where.Append(") ");
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

        for (var i = 0; i < tokens.Length; i++)
        {
            cmd.Parameters.Add(new SqlParameter($"@t{i}", SqlDbType.NVarChar, 128) { Value = "%" + tokens[i] + "%" });
        }

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
                ExampleJson: null));
        }

        return list;
    }

    private static IReadOnlyList<string> BuildQueryVariants(string query)
    {
        var list = new List<string>(capacity: 5);

        if (!string.IsNullOrWhiteSpace(query))
        {
            list.Add(query);
            var norm = Normalize(query);
            if (!string.Equals(norm, query, StringComparison.Ordinal))
                list.Add(norm);

            var ascii = RemoveDiacritics(norm);
            if (!string.Equals(ascii, norm, StringComparison.Ordinal))
                list.Add(ascii);

            // Minimal bilingual normalization (avoid "hard-code intent"); keep it tiny and extensible.
            var mapped = ascii
                .Replace("bao nhieu", "count", StringComparison.OrdinalIgnoreCase)
                .Replace("dem", "count", StringComparison.OrdinalIgnoreCase)
                .Replace("tong so", "total", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(mapped, ascii, StringComparison.OrdinalIgnoreCase))
                list.Add(mapped);
        }

        return list;
    }

    private static IEnumerable<string> Tokenize(IReadOnlyList<string> variants)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var v in variants)
        {
            foreach (var t in SplitTokens(v))
            {
                if (t.Length < 2) continue;
                if (set.Add(t))
                    yield return t;
            }
        }
    }

    private static IEnumerable<string> SplitTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch) || ch is '/' or '_' or '.')
                sb.Append(ch);
            else
                sb.Append(' ');
        }

        foreach (var raw in sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            yield return raw;
    }

    private static int Score(AtomicCatalogEntry entry, IReadOnlyList<string> variants, string[] tokens)
    {
        var score = 0;

        var sp = entry.SpName ?? string.Empty;
        var tags = entry.Tags ?? string.Empty;
        var intent = (entry.IntentEn ?? string.Empty) + " " + (entry.IntentVi ?? string.Empty);
        var domain = entry.Domain ?? string.Empty;
        var entity = entry.Entity ?? string.Empty;
        var paramsJson = entry.ParamsJson ?? string.Empty;

        foreach (var v in variants)
        {
            if (!string.IsNullOrWhiteSpace(v) &&
                sp.Equals(v.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
                break;
            }
        }

        foreach (var t in tokens)
        {
            if (sp.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 200;
            if (tags.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 80;
            if (intent.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 60;
            if (domain.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 40;
            if (entity.Contains(t, StringComparison.OrdinalIgnoreCase)) score += 120;
        }

        // Query hints: season pattern + presence of Season param
        if (tokens.Any(t => t.Contains("/", StringComparison.Ordinal)) && paramsJson.Contains("Season", StringComparison.OrdinalIgnoreCase))
            score += 120;

        if (tokens.Any(t => t.Equals("count", StringComparison.OrdinalIgnoreCase) || t.Equals("total", StringComparison.OrdinalIgnoreCase) || t.Equals("bao", StringComparison.OrdinalIgnoreCase)) &&
            tags.Contains("count", StringComparison.OrdinalIgnoreCase))
            score += 60;

        // Prefer procedures with usable param metadata.
        if (!string.IsNullOrWhiteSpace(entry.ParamsJson)) score += 10;
        if (!string.IsNullOrWhiteSpace(entry.ExampleJson)) score += 5;

        return score;
    }

    private static string Normalize(string text)
    {
        return string.Join(' ', (text ?? string.Empty)
            .Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var normalized = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(normalized.Length);

        foreach (var ch in normalized)
        {
            var uc = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch);
            if (uc != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
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
