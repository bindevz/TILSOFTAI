using Microsoft.EntityFrameworkCore;
using TILSOFTAI.Domain.Entities;
using TILSOFTAI.Domain.Interfaces;

namespace TILSOFTAI.Infrastructure.Data;

public sealed class SqlServerDbContext : DbContext, IUnitOfWork
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<ProductModel> ProductModels => Set<ProductModel>();
    public DbSet<ProductModelAttribute> ProductModelAttributes => Set<ProductModelAttribute>();
    public DbSet<ConfirmationPlanEntity> ConfirmationPlans => Set<ConfirmationPlanEntity>();

    public SqlServerDbContext(DbContextOptions<SqlServerDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(o => o.Id);
            entity.Property(o => o.Status).HasConversion<int>();
            entity.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");
            entity.Property(o => o.Currency).HasMaxLength(3).IsUnicode(false);
            entity.Property(o => o.TenantId).HasMaxLength(64).IsRequired();
            entity.Property(o => o.Reference).HasMaxLength(128);
            entity.HasIndex(o => new { o.TenantId, o.OrderDate });
            entity.HasIndex(o => new { o.TenantId, o.CustomerId });
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.ToTable("Customers");
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Email).HasMaxLength(320);
            entity.Property(c => c.Name).HasMaxLength(200);
            entity.Property(c => c.TenantId).HasMaxLength(64).IsRequired();
            entity.HasIndex(c => new { c.TenantId, c.Email }).IsUnique();
        });

        modelBuilder.ApplyConfiguration(new TILSOFTAI.Infrastructure.Data.Configurations.ProductModelConfiguration());
        modelBuilder.ApplyConfiguration(new TILSOFTAI.Infrastructure.Data.Configurations.ProductModelAttributeConfiguration());
        modelBuilder.ApplyConfiguration(new TILSOFTAI.Infrastructure.Data.Configurations.ConfirmationPlanConfiguration());
    }

    public async Task ExecuteTransactionalAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        await operation(cancellationToken);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
