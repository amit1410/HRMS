using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

internal static class SeparationClearanceConfigurationHelpers
{
    public static void Tenant<TEntity>(EntityTypeBuilder<TEntity> b) where TEntity : class, HRMS.Domain.Common.ITenantEntity
    {
        b.Property(x => x.TenantId).IsRequired();
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationClearanceTemplateConfiguration : IEntityTypeConfiguration<SeparationClearanceTemplate>
{
    public void Configure(EntityTypeBuilder<SeparationClearanceTemplate> b)
    {
        b.ToTable("SeparationClearanceTemplates"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b);
        b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.AppliesToSeparationType).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EffectiveFrom, x.EffectiveTo });
        b.HasMany(x => x.Items).WithOne(x => x.Template).HasForeignKey(x => new { x.TenantId, x.TemplateId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SeparationClearanceTemplateItemConfiguration : IEntityTypeConfiguration<SeparationClearanceTemplateItem>
{
    public void Configure(EntityTypeBuilder<SeparationClearanceTemplateItem> b)
    {
        b.ToTable("SeparationClearanceTemplateItems"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b);
        b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.Category).HasConversion<int>(); b.Property(x => x.OwnerType).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.TemplateId, x.Code }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.TemplateId, x.Sequence });
    }
}

public sealed class SeparationClearanceConfiguration : IEntityTypeConfiguration<SeparationClearance>
{
    public void Configure(EntityTypeBuilder<SeparationClearance> b)
    {
        b.ToTable("SeparationClearances"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b);
        b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1);
        b.HasIndex(x => new { x.TenantId, x.EmployeeSeparationId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasOne<EmployeeSeparation>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeSeparationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Tasks).WithOne(x => x.Clearance).HasForeignKey(x => new { x.TenantId, x.SeparationClearanceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Events).WithOne(x => x.Clearance).HasForeignKey(x => new { x.TenantId, x.SeparationClearanceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationClearanceTaskConfiguration : IEntityTypeConfiguration<SeparationClearanceTask>
{
    public void Configure(EntityTypeBuilder<SeparationClearanceTask> b)
    {
        b.ToTable("SeparationClearanceTasks"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b);
        b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Category).HasConversion<int>(); b.Property(x => x.OwnerType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Comment).HasMaxLength(4000); b.Property(x => x.BlockingReason).HasMaxLength(2000); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1);
        b.HasIndex(x => new { x.TenantId, x.SeparationClearanceId }); b.HasIndex(x => new { x.TenantId, x.Status }); b.HasIndex(x => new { x.TenantId, x.AssignedUserId, x.Status }); b.HasIndex(x => new { x.TenantId, x.AssignedDepartmentId, x.Status }); b.HasIndex(x => new { x.TenantId, x.DueDate });
        b.HasOne<SeparationClearanceTemplateItem>().WithMany().HasForeignKey(x => new { x.TenantId, x.TemplateItemId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Assets).WithOne().HasForeignKey(x => new { x.TenantId, x.SeparationClearanceTaskId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationAssetReturnConfiguration : IEntityTypeConfiguration<SeparationAssetReturn>
{
    public void Configure(EntityTypeBuilder<SeparationAssetReturn> b)
    {
        b.ToTable("SeparationAssetReturns"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b);
        b.Property(x => x.AssetReference).HasMaxLength(120).IsRequired(); b.Property(x => x.AssetType).HasMaxLength(120).IsRequired(); b.Property(x => x.AssetName).HasMaxLength(200).IsRequired(); b.Property(x => x.SerialNumber).HasMaxLength(120); b.Property(x => x.ReturnStatus).HasConversion<int>(); b.Property(x => x.Condition).HasConversion<int>(); b.Property(x => x.RecoveryReference).HasMaxLength(200); b.Property(x => x.Comment).HasMaxLength(2000); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1);
        b.HasIndex(x => new { x.TenantId, x.SeparationClearanceTaskId }); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ReturnStatus });
    }
}

public sealed class SeparationClearanceEventConfiguration : IEntityTypeConfiguration<SeparationClearanceEvent>
{
    public void Configure(EntityTypeBuilder<SeparationClearanceEvent> b)
    {
        b.ToTable("SeparationClearanceEvents"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.FromStatus).HasConversion<int>(); b.Property(x => x.ToStatus).HasConversion<int>(); b.Property(x => x.FromTaskStatus).HasConversion<int>(); b.Property(x => x.ToTaskStatus).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(2000); b.Property(x => x.Comment).HasMaxLength(4000); b.Property(x => x.MetadataJson).HasMaxLength(8000); b.HasIndex(x => new { x.TenantId, x.SeparationClearanceId, x.OccurredAtUtc }); b.HasIndex(x => new { x.TenantId, x.TaskId, x.OccurredAtUtc });
    }
}
