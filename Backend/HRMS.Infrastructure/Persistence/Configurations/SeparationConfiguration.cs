using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class SeparationReasonConfiguration : IEntityTypeConfiguration<SeparationReason>
{
    public void Configure(EntityTypeBuilder<SeparationReason> b)
    {
        b.ToTable("SeparationReasons"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(2000); b.Property(x => x.Category).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeSeparationConfiguration : IEntityTypeConfiguration<EmployeeSeparation>
{
    public void Configure(EntityTypeBuilder<EmployeeSeparation> b)
    {
        b.ToTable("EmployeeSeparations"); b.HasKey(x => x.Id); b.Property(x => x.SeparationNumber).HasMaxLength(80).IsRequired(); b.Property(x => x.SeparationType).HasConversion<int>(); b.Property(x => x.InitiatedBy).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.NoticeDisposition).HasConversion<int>(); b.Property(x => x.EmployeeRemarks).HasMaxLength(4000); b.Property(x => x.ManagerRemarks).HasMaxLength(4000); b.Property(x => x.HrRemarks).HasMaxLength(4000); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken(); b.HasIndex(x => new { x.TenantId, x.SeparationNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status }); b.HasIndex(x => new { x.TenantId, x.ActiveEmployeeKey }).IsUnique().HasFilter("[ActiveEmployeeKey] IS NOT NULL"); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Employee).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Reason).WithMany(x => x.Separations).HasForeignKey(new[] { "TenantId", "ReasonId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class EmployeeSeparationEventConfiguration : IEntityTypeConfiguration<EmployeeSeparationEvent>
{
    public void Configure(EntityTypeBuilder<EmployeeSeparationEvent> b)
    {
        b.ToTable("EmployeeSeparationEvents"); b.HasKey(x => x.Id); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.FromStatus).HasConversion<int>(); b.Property(x => x.ToStatus).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(2000); b.Property(x => x.Comment).HasMaxLength(4000); b.Property(x => x.MetadataJson).HasMaxLength(8000); b.HasIndex(x => new { x.TenantId, x.EmployeeSeparationId, x.OccurredAtUtc }); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Separation).WithMany(x => x.Events).HasForeignKey(new[] { "TenantId", "EmployeeSeparationId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
