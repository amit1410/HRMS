using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Durable claim/delivery/execution log for confirmation-review reminders. The unique
/// <c>(TenantId, OccurrenceKey)</c> index is what makes the claim atomic and dedup reliable, exactly as for
/// <see cref="LeaveReminderDeliveryConfiguration"/>.
/// </summary>
public sealed class ConfirmationReminderDeliveryConfiguration : IEntityTypeConfiguration<ConfirmationReminderDelivery>
{
    public void Configure(EntityTypeBuilder<ConfirmationReminderDelivery> b)
    {
        b.ToTable("ConfirmationReminderDeliveries");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.OccurrenceKey).HasMaxLength(160).IsRequired();
        b.Property(x => x.NotificationType).HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.RecipientType).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(x => x.RecipientEmail).HasMaxLength(256);
        b.Property(x => x.DueAtUtc).IsRequired();
        b.Property(x => x.AttemptCount).IsRequired();
        b.Property(x => x.ClaimToken);
        b.Property(x => x.LastError).HasMaxLength(500);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.OccurrenceKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ProbationEndDate });
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RecipientEmployee).WithMany().HasForeignKey(x => new { x.TenantId, x.RecipientEmployeeId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>One scheduler-configuration row per tenant (unique on <c>TenantId</c>).</summary>
public sealed class ConfirmationSchedulerSettingConfiguration : IEntityTypeConfiguration<ConfirmationSchedulerSetting>
{
    public void Configure(EntityTypeBuilder<ConfirmationSchedulerSetting> b)
    {
        b.ToTable("ConfirmationSchedulerSettings");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ModifiedBy).HasMaxLength(256);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => x.TenantId).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
