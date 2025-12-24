using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Domain.Interfaces;
using TILSOFTAI.Domain.ValueObjects;

namespace TILSOFTAI.Infrastructure.Data;

public sealed class SqlConfirmationPlanStore : IConfirmationPlanStore
{
    private readonly SqlServerDbContext _dbContext;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public SqlConfirmationPlanStore(SqlServerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveAsync(ConfirmationPlan plan, CancellationToken cancellationToken)
    {
        var entity = new ConfirmationPlanEntity
        {
            Id = plan.Id,
            Tool = plan.Tool,
            TenantId = plan.TenantId,
            UserId = plan.UserId,
            ExpiresAt = plan.ExpiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            DataJson = JsonSerializer.Serialize(plan.Data, _jsonOptions)
        };

        _dbContext.ConfirmationPlans.Update(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ConfirmationPlan?> GetAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.ConfirmationPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (entity.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            await RemoveAsync(id, cancellationToken);
            return null;
        }

        var data = DeserializeData(entity.DataJson);
        return new ConfirmationPlan
        {
            Id = entity.Id,
            Tool = entity.Tool,
            TenantId = entity.TenantId,
            UserId = entity.UserId,
            ExpiresAt = entity.ExpiresAt,
            Data = data
        };
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.ConfirmationPlans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (entity is null)
        {
            return;
        }

        _dbContext.ConfirmationPlans.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private IReadOnlyDictionary<string, string> DeserializeData(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions);
            return parsed is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Invalid confirmation payload.");
        }
    }
}
