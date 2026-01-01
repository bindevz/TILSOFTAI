using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using TILSOFTAI.Domain.Entities;
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

    public async Task<PagedResult<Model>> SearchAsync(string tenantId, string? rangeName, string? modelCode, string? modelName, string? season, string? collection, int page, int size, CancellationToken cancellationToken)
    {
        //try
        //{
        //    var connString = _dbContext.Database.GetConnectionString();
        //    await using var conn = new SqlConnection(connString);
        //    await conn.OpenAsync(cancellationToken);

        //    if (conn.State != ConnectionState.Open)
        //        await conn.OpenAsync(cancellationToken);
        //}
        //catch (Exception ex)
        //{
        //    Console.Write(ex.Message);
        //    throw;
        //}
        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        if (conn.State != ConnectionState.Open)
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

        var items = new List<Model>();
        int total = 0;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);


        // Cache ordinals một lần
        var ordModelId = reader.GetOrdinal("ModelID");
        var ordModelUd = reader.GetOrdinal("ModelUD");
        var ordModelNm = reader.GetOrdinal("ModelNM");
        var ordSeason = reader.GetOrdinal("Season");
        var ordCollection = reader.GetOrdinal("Collection");
        var ordRangeName = reader.GetOrdinal("RangeName");

        static string GetStringOrEmpty(SqlDataReader r, int ord)
            => r.IsDBNull(ord) ? string.Empty : r.GetString(ord);

        static int GetInt32Safe(SqlDataReader r, int ord)
        {
            if (r.IsDBNull(ord)) return 0;

            // Nếu DB trả bigint/decimal thì Convert sẽ “chịu” tốt hơn GetInt32 trực tiếp
            return Convert.ToInt32(r.GetValue(ord));
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new Model
            {
                ModelID = GetInt32Safe(reader, ordModelId),
                ModelUD = GetStringOrEmpty(reader, ordModelUd),
                ModelNM = GetStringOrEmpty(reader, ordModelNm),
                Season = GetStringOrEmpty(reader, ordSeason),
                Collection = GetStringOrEmpty(reader, ordCollection),
                RangeName = GetStringOrEmpty(reader, ordRangeName)
            });
        }

        // Result set 2: TotalCount
        if (await reader.NextResultAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                total = reader.GetInt32(reader.GetOrdinal("TotalCount"));
            }
        }

        return new PagedResult<Model>
        {
            Items = items,
            TotalCount = total,
            PageNumber = page,
            PageSize = size
        };
    }

    public Task<Model?> GetAsync(string tenantId, Guid id, CancellationToken cancellationToken)
    {
        return _dbContext.Models
            .AsNoTracking()
            .Include(m => m.Attributes)
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.ModelID == 1, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ModelAttribute>> ListAttributesAsync(string tenantId, Guid modelId, CancellationToken cancellationToken)
    {
        return await _dbContext.ModelAttributes
            .AsNoTracking()
            .Where(a => a.ModelId == modelId && a.Model.TenantId == tenantId)
            .OrderBy(a => a.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<PriceAnalysis> AnalyzePriceAsync(string tenantId, Guid modelId, CancellationToken cancellationToken)
    {
        var model = await _dbContext.Models
            .AsNoTracking()
            .Include(m => m.Attributes)
            .FirstOrDefaultAsync(m => m.ModelID == 1 && m.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");

        var adjustment = model.Attributes.Count * 5m;
        var final = model.BasePrice + adjustment;
        return new PriceAnalysis(model.BasePrice, adjustment, final);
    }

    public async Task CreateAsync(Model model, CancellationToken cancellationToken)
    {
        await _dbContext.Models.AddAsync(model, cancellationToken);
    }
}
