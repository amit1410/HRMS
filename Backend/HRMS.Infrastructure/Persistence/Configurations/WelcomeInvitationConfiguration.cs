using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class WelcomeInvitationBatchConfiguration : IEntityTypeConfiguration<WelcomeInvitationBatch>
{
    public void Configure(EntityTypeBuilder<WelcomeInvitationBatch> builder)
    {
        builder.ToTable("WelcomeInvitationBatches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.RequestedAtUtc });
        builder.HasMany(x => x.Attempts).WithOne(x => x.Batch).HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class WelcomeInvitationAttemptConfiguration : IEntityTypeConfiguration<WelcomeInvitationAttempt>
{
    public void Configure(EntityTypeBuilder<WelcomeInvitationAttempt> builder)
    {
        builder.ToTable("WelcomeInvitationAttempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RecipientEmailSnapshot).HasMaxLength(320).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ErrorCategory).HasMaxLength(64);
        builder.Property(x => x.ErrorSummary).HasMaxLength(500);
        builder.Property(x => x.LeaseId).HasMaxLength(64);
        builder.Property(x => x.MaxAttempts).HasDefaultValue(3);
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.RequestedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.DeliveryStatus, x.NextAttemptAtUtc, x.LeaseExpiresAtUtc })
            .HasDatabaseName("IX_WelcomeInvitationAttempts_Tenant_Status_Next_LeaseExp");
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PortalUser).WithMany().HasForeignKey(x => x.PortalUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
