using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;
using TILSOFTAI.Infrastructure.Data;

namespace TILSOFTAI.Infrastructure.Repositories;

/// <summary>
/// Reads value-hints for filters.catalog via a stored procedure.
/// </summary>
public sealed class FilterValueHintsRepository : IFilterValueHintsRepository
{
    private readonly SqlServerDbContext _dbContext;

    public FilterValueHintsRepository(SqlServerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<FilterValueHintRow>> GetValueHintsAsync(
        string tenantId,
        string resource,
        int top,
        CancellationToken cancellationToken)
    {
        var results = new List<FilterValueHintRow>();

        var connString = _dbContext.Database.GetConnectionString();
        await using var conn = new SqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "dbo.TILSOFTAI_sp_filters_value_hints";
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add(new SqlParameter("@TenantId", SqlDbType.VarChar, 50) { Value = tenantId });
        cmd.Parameters.Add(new SqlParameter("@Resource", SqlDbType.VarChar, 100) { Value = resource });
        cmd.Parameters.Add(new SqlParameter("@Top", SqlDbType.Int) { Value = Math.Clamp(top, 1, 200) });

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        // Expected schema: FilterKey (nvarchar), Value (nvarchar), Count (int), Label (nvarchar, nullable)
        var ordKey = reader.GetOrdinal("FilterKey");
        var ordVal = reader.GetOrdinal("Value");
        var ordCnt = reader.GetOrdinal("Count");
        var ordLbl = SafeOrdinal(reader, "Label");

        while (await reader.ReadAsync(cancellationToken))
        {
            var key = reader.IsDBNull(ordKey) ? string.Empty : reader.GetString(ordKey);
            var val = reader.IsDBNull(ordVal) ? string.Empty : reader.GetString(ordVal);
            var cnt = reader.IsDBNull(ordCnt) ? 0 : Convert.ToInt32(reader.GetValue(ordCnt));
            var lbl = ordLbl >= 0 && !reader.IsDBNull(ordLbl) ? reader.GetString(ordLbl) : null;

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(val))
                continue;

            results.Add(new FilterValueHintRow(key.Trim(), val.Trim(), cnt, lbl));
        }

        return results;
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
}
