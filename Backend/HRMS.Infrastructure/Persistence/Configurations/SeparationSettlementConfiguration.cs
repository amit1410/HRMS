using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class SeparationSettlementOrchestrationConfiguration : IEntityTypeConfiguration<SeparationSettlementOrchestration>
{
    public void Configure(EntityTypeBuilder<SeparationSettlementOrchestration> b)
    {
        b.ToTable("SeparationSettlementOrchestrations");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.ExitInterviewDispositionSnapshot).HasMaxLength(120);
        b.Property(x => x.LastFailureCode).HasMaxLength(120);
        b.Property(x => x.LastFailureMessage).HasMaxLength(2000);
        b.Property(x => x.IdempotencyKey).HasMaxLength(200);
        b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1);
        b.HasIndex(x => new { x.TenantId, x.EmployeeSeparationId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.PayrollFinalSettlementId }).IsUnique().HasFilter("[PayrollFinalSettlementId] IS NOT NULL");
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EmployeeSeparation).WithMany().HasForeignKey(new[] { "TenantId", "EmployeeSeparationId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Events).WithOne(x => x.Orchestration).HasForeignKey(new[] { "TenantId", "SeparationSettlementOrchestrationId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationSettlementEventConfiguration : IEntityTypeConfiguration<SeparationSettlementEvent>
{
    public void Configure(EntityTypeBuilder<SeparationSettlementEvent> b)
    {
        b.ToTable("SeparationSettlementEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.EventType).HasConversion<int>();
        b.Property(x => x.FromStatus).HasConversion<int>();
        b.Property(x => x.ToStatus).HasConversion<int>();
        b.Property(x => x.Reason).HasMaxLength(2000);
        b.Property(x => x.MetadataJson).HasMaxLength(8000);
        b.HasIndex(x => new { x.TenantId, x.SeparationSettlementOrchestrationId, x.OccurredAtUtc });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
