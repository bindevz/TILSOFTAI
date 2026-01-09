using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Data;

namespace TILSOFTAI.Infrastructure.Repositories;

public sealed class ModelRepository : IModelRepository
{
    private readonly SqlServerDbContext _dbContext;

    public ModelRepository(SqlServerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TabularData> SearchTabularAsync(
        string tenantId,
        string? rangeName,
        string? modelCode,
        string? modelName,
        string? season,
        string? collection,
        int page,
        int size,
        CancellationToken cancellationToken)
    {
        // Same stored procedure as SearchAsync, but without mapping to domain entities.
        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "dbo.TILSOFTAI_sp_models_search";
        cmd.CommandType = CommandType.StoredProcedure;

        cmd.Parameters.Add(new SqlParameter("@RangeName", SqlDbType.VarChar, 50) { Value = (object?)rangeName ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ModelCode", SqlDbType.VarChar, 4) { Value = (object?)modelCode ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ModelName", SqlDbType.VarChar, 200) { Value = (object?)modelName ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Season", SqlDbType.VarChar, 9) { Value = (object?)season ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Collection", SqlDbType.VarChar, 50) { Value = (object?)collection ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Page", SqlDbType.Int) { Value = page });
        cmd.Parameters.Add(new SqlParameter("@Size", SqlDbType.Int) { Value = size });

        var rows = new List<object?[]>(capacity: Math.Min(size, 2048));
        int? total = null;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        // We keep a small, stable column surface for ver-23 (can be extended later by Semantic Catalog).
        // If the stored procedure adds new columns, we ignore them unless explicitly mapped.
        var ordModelId = SafeOrdinal(reader, "ModelID");
        var ordModelUd = SafeOrdinal(reader, "ModelUD");
        var ordModelNm = SafeOrdinal(reader, "ModelNM");
        var ordSeason = SafeOrdinal(reader, "Season");
        var ordCollection = SafeOrdinal(reader, "Collection");
        var ordRangeName = SafeOrdinal(reader, "RangeName");

        while (await reader.ReadAsync(cancellationToken))
        {
            object? Get(int ord) => ord < 0 || reader.IsDBNull(ord) ? null : reader.GetValue(ord);

            rows.Add(new object?[]
            {
                Get(ordModelId),
                Get(ordModelUd),
                Get(ordModelNm),
                Get(ordSeason),
                Get(ordCollection),
                Get(ordRangeName)
            });
        }

        // Result set 2: TotalCount
        if (await reader.NextResultAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                var ordTotal = SafeOrdinal(reader, "TotalCount");
                if (ordTotal >= 0 && !reader.IsDBNull(ordTotal))
                    total = Convert.ToInt32(reader.GetValue(ordTotal));
            }
        }

        var cols = new List<TabularColumn>
        {
            new("modelId", TabularType.Int32),
            new("modelCode", TabularType.String),
            new("modelName", TabularType.String),
            new("season", TabularType.String),
            new("collection", TabularType.String),
            new("rangeName", TabularType.String)
        };

        return new TabularData(cols, rows, total);
    }

    public async Task<ModelsStatsResult> GetStatsAsync(
        string tenantId,
        string? rangeName,
        string? modelCode,
        string? modelName,
        string? season,
        string? collection,
        int topN,
        CancellationToken cancellationToken)
    {
        // Expected stored procedure contract:
        // RS1: TotalCount (int)
        // RS2: RangeName breakdown: Key, Label (nullable), Count
        // RS3: Collection breakdown: Key, Label (nullable), Count
        // RS4: Season breakdown: Key, Label (nullable), Count

        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "dbo.TILSOFTAI_sp_models_stats_v1";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add(new SqlParameter("@RangeName", SqlDbType.VarChar, 50) { Value = (object?)rangeName ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ModelCode", SqlDbType.VarChar, 4) { Value = (object?)modelCode ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@ModelName", SqlDbType.VarChar, 200) { Value = (object?)modelName ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Season", SqlDbType.VarChar, 9) { Value = (object?)season ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@Collection", SqlDbType.VarChar, 50) { Value = (object?)collection ?? DBNull.Value });
        cmd.Parameters.Add(new SqlParameter("@TopN", SqlDbType.Int) { Value = Math.Clamp(topN, 1, 50) });

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var total = 0;
            if (await reader.ReadAsync(cancellationToken))
            {
                var ordTotal = SafeOrdinal(reader, "TotalCount");
                if (ordTotal >= 0 && !reader.IsDBNull(ordTotal))
                    total = Convert.ToInt32(reader.GetValue(ordTotal));
            }

            var breakdowns = new List<ModelsStatsBreakdown>();

            if (await reader.NextResultAsync(cancellationToken))
            {
                breakdowns.Add(new ModelsStatsBreakdown(
                    Dimension: "rangeName",
                    Title: "Range/Category",
                    Items: await ReadBreakdownItemsAsync(reader, cancellationToken)));
            }

            if (await reader.NextResultAsync(cancellationToken))
            {
                breakdowns.Add(new ModelsStatsBreakdown(
                    Dimension: "collection",
                    Title: "Collection",
                    Items: await ReadBreakdownItemsAsync(reader, cancellationToken)));
            }

            if (await reader.NextResultAsync(cancellationToken))
            {
                breakdowns.Add(new ModelsStatsBreakdown(
                    Dimension: "season",
                    Title: "Season",
                    Items: await ReadBreakdownItemsAsync(reader, cancellationToken)));
            }

            return new ModelsStatsResult(total, breakdowns);
        }
        catch (SqlException ex) when (ex.Number == 2812) // could not find stored procedure
        {
            // Fallback for environments where the enterprise stored procedure isn't deployed yet.
            var search = await SearchTabularAsync(tenantId, rangeName, modelCode, modelName, season, collection, page: 1, size: 1, cancellationToken);
            return new ModelsStatsResult(search.TotalCount ?? 0, Array.Empty<ModelsStatsBreakdown>());
        }
    }

    public async Task<ModelsOptionsResult> GetOptionsAsync(
        string tenantId,
        int modelId,
        bool includeConstraints,
        CancellationToken cancellationToken)
    {
        // Expected stored procedure contract:
        // RS1: Model header: ModelID, ModelUD, ModelNM, Season, Collection, RangeName
        // RS2: Option groups: GroupKey, GroupName, IsRequired (bit/int), SortOrder
        // RS3: Option values: GroupKey, ValueKey, ValueName, SortOrder, Note (nullable)
        // RS4 (optional): Constraints: RuleType, IfGroupKey, IfValueKey, ThenGroupKey, ThenValueKey, Message (nullable)

        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "dbo.TILSOFTAI_sp_models_options_v1";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.VarChar, 50) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@ModelId", SqlDbType.Int) { Value = modelId });
        cmd.Parameters.Add(new SqlParameter("@IncludeConstraints", SqlDbType.Bit) { Value = includeConstraints });

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            // RS1 - header
            ModelHeader? header = null;
            if (await reader.ReadAsync(cancellationToken))
            {
                var ordId = reader.GetOrdinal("ModelID");
                var ordCode = SafeOrdinal(reader, "ModelUD");
                var ordName = SafeOrdinal(reader, "ModelNM");
                var ordSeason = SafeOrdinal(reader, "Season");
                var ordCollection = SafeOrdinal(reader, "Collection");
                var ordRangeName = SafeOrdinal(reader, "RangeName");

                header = new ModelHeader(
                    ModelId: reader.IsDBNull(ordId) ? modelId : Convert.ToInt32(reader.GetValue(ordId)),
                    ModelCode: ordCode >= 0 && !reader.IsDBNull(ordCode) ? reader.GetString(ordCode) : string.Empty,
                    ModelName: ordName >= 0 && !reader.IsDBNull(ordName) ? reader.GetString(ordName) : string.Empty,
                    Season: ordSeason >= 0 && !reader.IsDBNull(ordSeason) ? reader.GetString(ordSeason) : null,
                    Collection: ordCollection >= 0 && !reader.IsDBNull(ordCollection) ? reader.GetString(ordCollection) : null,
                    RangeName: ordRangeName >= 0 && !reader.IsDBNull(ordRangeName) ? reader.GetString(ordRangeName) : null);
            }

            header ??= new ModelHeader(modelId, string.Empty, string.Empty, null, null, null);

            var groups = new Dictionary<string, (string Name, bool Required, int Sort)>(StringComparer.OrdinalIgnoreCase);
            var valuesByGroup = new Dictionary<string, List<OptionValue>>(StringComparer.OrdinalIgnoreCase);
            var constraints = new List<OptionConstraint>();

            // RS2 - groups
            if (await reader.NextResultAsync(cancellationToken))
            {
                var ordKey = SafeOrdinal(reader, "GroupKey");
                var ordName = SafeOrdinal(reader, "GroupName");
                var ordReq = SafeOrdinal(reader, "IsRequired");
                var ordSort = SafeOrdinal(reader, "SortOrder");

                while (await reader.ReadAsync(cancellationToken))
                {
                    var key = ordKey >= 0 && !reader.IsDBNull(ordKey) ? reader.GetString(ordKey) : string.Empty;
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    var name = ordName >= 0 && !reader.IsDBNull(ordName) ? reader.GetString(ordName) : key;
                    var req = ordReq >= 0 && !reader.IsDBNull(ordReq) && Convert.ToInt32(reader.GetValue(ordReq)) == 1;
                    var sort = ordSort >= 0 && !reader.IsDBNull(ordSort) ? Convert.ToInt32(reader.GetValue(ordSort)) : 0;

                    groups[key] = (name, req, sort);
                    if (!valuesByGroup.ContainsKey(key))
                        valuesByGroup[key] = new List<OptionValue>();
                }
            }

            // RS3 - values
            if (await reader.NextResultAsync(cancellationToken))
            {
                var ordG = SafeOrdinal(reader, "GroupKey");
                var ordK = SafeOrdinal(reader, "ValueKey");
                var ordN = SafeOrdinal(reader, "ValueName");
                var ordS = SafeOrdinal(reader, "SortOrder");
                var ordNote = SafeOrdinal(reader, "Note");

                while (await reader.ReadAsync(cancellationToken))
                {
                    var g = ordG >= 0 && !reader.IsDBNull(ordG) ? reader.GetString(ordG) : string.Empty;
                    var k = ordK >= 0 && !reader.IsDBNull(ordK) ? reader.GetString(ordK) : string.Empty;
                    if (string.IsNullOrWhiteSpace(g) || string.IsNullOrWhiteSpace(k)) continue;
                    var n = ordN >= 0 && !reader.IsDBNull(ordN) ? reader.GetString(ordN) : k;
                    var s = ordS >= 0 && !reader.IsDBNull(ordS) ? Convert.ToInt32(reader.GetValue(ordS)) : 0;
                    var note = ordNote >= 0 && !reader.IsDBNull(ordNote) ? reader.GetString(ordNote) : null;

                    if (!valuesByGroup.TryGetValue(g, out var list))
                    {
                        list = new List<OptionValue>();
                        valuesByGroup[g] = list;
                    }
                    list.Add(new OptionValue(k, n, s, note));
                }
            }

            // RS4 - constraints (optional)
            if (includeConstraints && await reader.NextResultAsync(cancellationToken))
            {
                var ordT = SafeOrdinal(reader, "RuleType");
                var ordIG = SafeOrdinal(reader, "IfGroupKey");
                var ordIV = SafeOrdinal(reader, "IfValueKey");
                var ordTG = SafeOrdinal(reader, "ThenGroupKey");
                var ordTV = SafeOrdinal(reader, "ThenValueKey");
                var ordMsg = SafeOrdinal(reader, "Message");

                while (await reader.ReadAsync(cancellationToken))
                {
                    var ruleType = ordT >= 0 && !reader.IsDBNull(ordT) ? reader.GetString(ordT) : "disallow";
                    var ifG = ordIG >= 0 && !reader.IsDBNull(ordIG) ? reader.GetString(ordIG) : string.Empty;
                    var ifV = ordIV >= 0 && !reader.IsDBNull(ordIV) ? reader.GetString(ordIV) : string.Empty;
                    var thenG = ordTG >= 0 && !reader.IsDBNull(ordTG) ? reader.GetString(ordTG) : string.Empty;
                    var thenV = ordTV >= 0 && !reader.IsDBNull(ordTV) ? reader.GetString(ordTV) : string.Empty;
                    var msg = ordMsg >= 0 && !reader.IsDBNull(ordMsg) ? reader.GetString(ordMsg) : null;

                    if (string.IsNullOrWhiteSpace(ifG) || string.IsNullOrWhiteSpace(ifV) || string.IsNullOrWhiteSpace(thenG) || string.IsNullOrWhiteSpace(thenV))
                        continue;

                    constraints.Add(new OptionConstraint(ruleType, ifG, ifV, thenG, thenV, msg));
                }
            }

            var optionGroups = groups
                .OrderBy(kv => kv.Value.Sort)
                .Select(kv => new OptionGroup(
                    GroupKey: kv.Key,
                    GroupName: kv.Value.Name,
                    IsRequired: kv.Value.Required,
                    SortOrder: kv.Value.Sort,
                    Values: valuesByGroup.TryGetValue(kv.Key, out var list)
                        ? list.OrderBy(v => v.SortOrder).ToList()
                        : new List<OptionValue>()))
                .ToList();

            return new ModelsOptionsResult(header, optionGroups, constraints);
        }
        catch (SqlException ex) when (ex.Number == 2812)
        {
            // Enterprise SP not deployed yet.
            var header = new ModelHeader(modelId, string.Empty, string.Empty, null, null, null);
            return new ModelsOptionsResult(header, Array.Empty<OptionGroup>(), Array.Empty<OptionConstraint>());
        }
    }


    public async Task<PriceAnalysis> AnalyzePriceAsync(string tenantId, Guid modelId, CancellationToken cancellationToken)
    {
        var model = new Guid();
        var basePrice = 0;
        var adjustment = 1 * 5m;
        var final = 1 + adjustment;
        return new PriceAnalysis(basePrice, adjustment, final);
    }

    private static int SafeOrdinal(SqlDataReader reader, string name)
    {
        try
        {
            return reader.GetOrdinal(name);
        }
        catch (IndexOutOfRangeException)
        {
            return -1;
        }
    }

    private static async Task<IReadOnlyList<ModelsStatsBreakdownItem>> ReadBreakdownItemsAsync(SqlDataReader reader, CancellationToken ct)
    {
        var list = new List<ModelsStatsBreakdownItem>();
        var ordKey = SafeOrdinal(reader, "Key");
        var ordLabel = SafeOrdinal(reader, "Label");
        var ordCount = SafeOrdinal(reader, "Count");

        while (await reader.ReadAsync(ct))
        {
            var key = ordKey >= 0 && !reader.IsDBNull(ordKey) ? reader.GetString(ordKey) : string.Empty;
            if (string.IsNullOrWhiteSpace(key)) continue;
            var label = ordLabel >= 0 && !reader.IsDBNull(ordLabel) ? reader.GetString(ordLabel) : null;
            var cnt = ordCount >= 0 && !reader.IsDBNull(ordCount) ? Convert.ToInt32(reader.GetValue(ordCount)) : 0;
            list.Add(new ModelsStatsBreakdownItem(key, label, cnt));
        }

        return list;
    }
}
