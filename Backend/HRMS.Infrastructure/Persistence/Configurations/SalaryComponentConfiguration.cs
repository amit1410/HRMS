using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class SalaryComponentConfiguration : IEntityTypeConfiguration<SalaryComponent>
{
    public void Configure(EntityTypeBuilder<SalaryComponent> b)
    {
        b.ToTable("SalaryComponents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.ComponentType).HasConversion<int>().IsRequired();
        b.Property(x => x.CalculationType).HasConversion<int>().IsRequired();
        b.Property(x => x.StatutoryType).HasConversion<int>().IsRequired();
        b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.IsActive });
        b.HasIndex(x => new { x.TenantId, x.ComponentType });
        b.HasIndex(x => new { x.TenantId, x.EffectiveFrom, x.EffectiveTo });
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SalaryComponentHistoryConfiguration : IEntityTypeConfiguration<SalaryComponentHistory>
{
    public void Configure(EntityTypeBuilder<SalaryComponentHistory> b)
    {
        b.ToTable("SalaryComponentHistories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.ComponentType).HasConversion<int>().IsRequired();
        b.Property(x => x.CalculationType).HasConversion<int>().IsRequired();
        b.Property(x => x.StatutoryType).HasConversion<int>().IsRequired();
        b.HasIndex(x => new { x.TenantId, x.SalaryComponentId, x.ChangedAtUtc });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SalaryComponent).WithMany(x => x.History).HasForeignKey(x => new { x.TenantId, x.SalaryComponentId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => new { x.TenantId, x.ActorUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
