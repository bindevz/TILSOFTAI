using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TILSOFTAI.Domain.Entities;

namespace TILSOFTAI.Infrastructure.Data.Configurations;

public sealed class ProductModelConfiguration : IEntityTypeConfiguration<ProductModel>
{
    public void Configure(EntityTypeBuilder<ProductModel> builder)
    {
        builder.ToTable("ProductModels");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.TenantId).HasMaxLength(64).IsRequired();
        builder.Property(m => m.Name).HasMaxLength(200).IsRequired();
        builder.Property(m => m.Category).HasMaxLength(100).IsRequired();
        builder.Property(m => m.BasePrice).HasColumnType("decimal(18,2)");
        builder.HasMany(m => m.Attributes).WithOne(a => a.Model).HasForeignKey(a => a.ModelId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.TenantId, m.Name });
    }
}

public sealed class ProductModelAttributeConfiguration : IEntityTypeConfiguration<ProductModelAttribute>
{
    public void Configure(EntityTypeBuilder<ProductModelAttribute> builder)
    {
        builder.ToTable("ProductModelAttributes");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Value).HasMaxLength(200).IsRequired();
        builder.HasIndex(a => new { a.ModelId, a.Name });
    }
}
