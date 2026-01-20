using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Domain.Interfaces;

namespace TILSOFTAI.Infrastructure.Data;

public sealed class SqlServerDbContext : DbContext, IUnitOfWork
{
    public SqlServerDbContext(DbContextOptions<SqlServerDbContext> options) : base(options)
    {

    }

    public async Task ExecuteTransactionalAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        await operation(cancellationToken);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
