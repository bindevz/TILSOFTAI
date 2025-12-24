using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace TILSOFTAI.Infrastructure.Data.Configurations;

public sealed class ConfirmationPlanConfiguration : IEntityTypeConfiguration<ConfirmationPlanEntity>
{
    public void Configure(EntityTypeBuilder<ConfirmationPlanEntity> builder)
    {
        builder.ToTable("ConfirmationPlans");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasMaxLength(64).IsUnicode(false);
        builder.Property(p => p.Tool).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(p => p.TenantId).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.Property(p => p.UserId).HasMaxLength(128).IsUnicode(false).IsRequired();
        builder.Property(p => p.CreatedAt).IsRequired();
        builder.Property(p => p.ExpiresAt).IsRequired();
        builder.Property(p => p.DataJson).IsRequired();
        builder.HasIndex(p => p.ExpiresAt);
    }
}
