using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class SalaryStructureConfiguration : IEntityTypeConfiguration<SalaryStructure>
{
    public void Configure(EntityTypeBuilder<SalaryStructure> b)
    {
        b.ToTable("SalaryStructures");
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.IsActive });
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SalaryStructureVersionConfiguration : IEntityTypeConfiguration<SalaryStructureVersion>
{
    public void Configure(EntityTypeBuilder<SalaryStructureVersion> b)
    {
        b.ToTable("SalaryStructureVersions");
        b.HasKey(x => x.Id);
        // The aggregate master owns concurrency. Versions are immutable configuration snapshots;
        // their metadata is changed only through the master service under the master token.
        b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1);
        b.HasIndex(x => new { x.TenantId, x.SalaryStructureId, x.EffectiveFrom }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EffectiveFrom, x.EffectiveTo });
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SalaryStructure).WithMany(x => x.Versions)
            .HasForeignKey(x => new { x.TenantId, x.SalaryStructureId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SalaryStructureComponentConfiguration : IEntityTypeConfiguration<SalaryStructureComponent>
{
    public void Configure(EntityTypeBuilder<SalaryStructureComponent> b)
    {
        b.ToTable("SalaryStructureComponents");
        b.HasKey(x => x.Id);
        b.Property(x => x.CalculationType).HasConversion<int>().IsRequired();
        b.Property(x => x.Value).HasPrecision(18, 6);
        b.Property(x => x.MinimumAmount).HasPrecision(18, 6);
        b.Property(x => x.MaximumAmount).HasPrecision(18, 6);
        b.Property(x => x.Formula).HasMaxLength(2000);
        b.HasIndex(x => new { x.TenantId, x.SalaryStructureVersionId, x.SalaryComponentId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.SalaryStructureVersionId, x.Sequence });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SalaryStructureVersion).WithMany(x => x.Components)
            .HasForeignKey(x => new { x.TenantId, x.SalaryStructureVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.SalaryComponent).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.SalaryComponentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_SalaryStructureComponents_SalaryComponent");
        b.HasOne(x => x.PercentageOfComponent).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.PercentageOfComponentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_SalaryStructureComponents_PercentageBase");
    }
}

public sealed class SalaryStructureHistoryConfiguration : IEntityTypeConfiguration<SalaryStructureHistory>
{
    public void Configure(EntityTypeBuilder<SalaryStructureHistory> b)
    {
        b.ToTable("SalaryStructureHistories");
        b.HasKey(x => x.Id);
        b.Property(x => x.ChangeType).HasConversion<int>().IsRequired();
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.ComponentsJson).HasMaxLength(20000).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.SalaryStructureId, x.ChangedAtUtc });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SalaryStructure).WithMany(x => x.History)
            .HasForeignKey(x => new { x.TenantId, x.SalaryStructureId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SalaryStructureVersion).WithMany(x => x.History)
            .HasForeignKey(x => new { x.TenantId, x.SalaryStructureVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ActorUser).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ActorUserId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
