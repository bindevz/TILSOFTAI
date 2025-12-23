namespace TILSOFTAI.Domain.Interfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
    Task ExecuteTransactionalAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken);
}
