using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class LeaveReminderDeliveryConfiguration : IEntityTypeConfiguration<LeaveReminderDelivery>
{
    public void Configure(EntityTypeBuilder<LeaveReminderDelivery> b)
    {
        b.ToTable("LeaveReminderDeliveries");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.OccurrenceKey).HasMaxLength(120).IsRequired();
        b.Property(x => x.NotificationType).HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).HasMaxLength(20).IsRequired();
        b.Property(x => x.DueAtUtc).IsRequired();
        b.Property(x => x.AttemptCount).IsRequired();
        b.Property(x => x.ClaimToken);
        b.Property(x => x.LastError).HasMaxLength(500);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.OccurrenceKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.LeaveRequestId, x.Status });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeaveRequest).WithMany().HasForeignKey(x => new { x.TenantId, x.LeaveRequestId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.RecipientEmployee).WithMany().HasForeignKey(x => new { x.TenantId, x.RecipientEmployeeId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
