using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

internal static class AttendanceWorkflowConfigurationHelpers
{
    public static void Tenant<T>(EntityTypeBuilder<T> b) where T : HRMS.Domain.Common.BaseEntity, HRMS.Domain.Common.ITenantEntity
    {
        b.Property(x => x.TenantId).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
    }
}

public sealed class AttendanceRegularizationRequestConfiguration : IEntityTypeConfiguration<AttendanceRegularizationRequest>
{
    public void Configure(EntityTypeBuilder<AttendanceRegularizationRequest> b)
    {
        b.ToTable("AttendanceRegularizationRequests"); b.HasKey(x => x.Id); AttendanceWorkflowConfigurationHelpers.Tenant(b);
        b.Property(x => x.RequestType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken(); b.Property(x => x.Reason).HasMaxLength(2000).IsRequired(); b.Property(x => x.ReviewerComments).HasMaxLength(2000);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.BusinessDate, x.Status }); b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubmittedByUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AttendanceRegularizationEventConfiguration : IEntityTypeConfiguration<AttendanceRegularizationEvent>
{
    public void Configure(EntityTypeBuilder<AttendanceRegularizationEvent> b)
    {
        b.ToTable("AttendanceRegularizationEvents"); b.HasKey(x => x.Id); AttendanceWorkflowConfigurationHelpers.Tenant(b); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.Comments).HasMaxLength(2000);
        b.HasIndex(x => new { x.TenantId, x.AttendanceRegularizationRequestId, x.OccurredAtUtc }); b.HasOne(x => x.Request).WithMany(x => x.Events).HasForeignKey(x => new { x.TenantId, x.AttendanceRegularizationRequestId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.ActorUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AttendanceAdjustmentConfiguration : IEntityTypeConfiguration<AttendanceAdjustment>
{
    public void Configure(EntityTypeBuilder<AttendanceAdjustment> b)
    {
        b.ToTable("AttendanceAdjustments"); b.HasKey(x => x.Id); AttendanceWorkflowConfigurationHelpers.Tenant(b); b.HasIndex(x => new { x.TenantId, x.AttendanceRegularizationRequestId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.BusinessDate });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<AttendanceRegularizationRequest>().WithMany().HasForeignKey(x => new { x.TenantId, x.AttendanceRegularizationRequestId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.ApprovedByUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AttendanceOnDutyRequestConfiguration : IEntityTypeConfiguration<AttendanceOnDutyRequest>
{
    public void Configure(EntityTypeBuilder<AttendanceOnDutyRequest> b)
    {
        b.ToTable("AttendanceOnDutyRequests"); b.HasKey(x => x.Id); AttendanceWorkflowConfigurationHelpers.Tenant(b); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).HasDefaultValue(1).IsConcurrencyToken(); b.Property(x => x.Reason).HasMaxLength(2000).IsRequired(); b.Property(x => x.Purpose).HasMaxLength(1000); b.Property(x => x.Location).HasMaxLength(500); b.Property(x => x.ReviewerComments).HasMaxLength(2000);
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.StartDate, x.EndDate, x.Status }); b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.SubmittedByUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AttendanceOnDutyEventConfiguration : IEntityTypeConfiguration<AttendanceOnDutyEvent>
{
    public void Configure(EntityTypeBuilder<AttendanceOnDutyEvent> b)
    {
        b.ToTable("AttendanceOnDutyEvents"); b.HasKey(x => x.Id); AttendanceWorkflowConfigurationHelpers.Tenant(b); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.Comments).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.AttendanceOnDutyRequestId, x.OccurredAtUtc }); b.HasOne(x => x.Request).WithMany(x => x.Events).HasForeignKey(x => new { x.TenantId, x.AttendanceOnDutyRequestId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.ActorUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
