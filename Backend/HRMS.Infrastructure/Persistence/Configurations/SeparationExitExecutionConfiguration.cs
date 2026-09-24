using HRMS.Domain.Entities.Separation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class SeparationExitExecutionConfiguration : IEntityTypeConfiguration<SeparationExitExecution>
{
    public void Configure(EntityTypeBuilder<SeparationExitExecution> builder)
    {
        builder.ToTable("SeparationExitExecutions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasConversion<int>().IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(100);
        builder.Property(x => x.FailureMessage).HasMaxLength(2000);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);
        builder.Property(x => x.SnapshotJson).HasMaxLength(12000);
        builder.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1);
        builder.HasIndex(x => new { x.TenantId, x.EmployeeSeparationId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Status });
        builder.HasOne(x => x.EmployeeSeparation).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeSeparationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationExitExecutionEventConfiguration : IEntityTypeConfiguration<SeparationExitExecutionEvent>
{
    public void Configure(EntityTypeBuilder<SeparationExitExecutionEvent> builder)
    {
        builder.ToTable("SeparationExitExecutionEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasConversion<int>().IsRequired();
        builder.Property(x => x.Step).HasMaxLength(100);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.MetadataJson).HasMaxLength(4000);
        builder.HasIndex(x => new { x.TenantId, x.SeparationExitExecutionId, x.EventType }).IsUnique();
        builder.HasOne(x => x.Execution).WithMany(x => x.Events).HasForeignKey(x => x.SeparationExitExecutionId).OnDelete(DeleteBehavior.Restrict);
    }
}
