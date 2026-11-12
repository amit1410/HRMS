using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class EmployeeCodeConfigurationAuditEventConfiguration : IEntityTypeConfiguration<EmployeeCodeConfigurationAuditEvent>
{
    public void Configure(EntityTypeBuilder<EmployeeCodeConfigurationAuditEvent> builder)
    {
        builder.ToTable("EmployeeCodeConfigurationAuditEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.EmployeeCodeConfigId).IsRequired();
        builder.Property(x => x.EmployeeCodeConfigVersionId).IsRequired();
        builder.Property(x => x.ActorUserId).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(40).IsRequired();
        builder.Property(x => x.OccurredAtUtc).IsRequired();
        builder.Property(x => x.OldValue).HasMaxLength(4000);
        builder.Property(x => x.NewValue).HasMaxLength(4000).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.EmployeeCodeConfigVersionId, x.OccurredAtUtc, x.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeeCodeConfigVersion>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EmployeeCodeConfigVersionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
