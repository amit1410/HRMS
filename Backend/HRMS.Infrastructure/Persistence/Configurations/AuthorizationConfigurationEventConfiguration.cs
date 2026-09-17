using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class AuthorizationConfigurationEventConfiguration : IEntityTypeConfiguration<AuthorizationConfigurationEvent>
{
    public void Configure(EntityTypeBuilder<AuthorizationConfigurationEvent> builder)
    {
        builder.ToTable("AuthorizationConfigurationEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasConversion<int>();
        builder.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PermissionCode).HasMaxLength(150);
        builder.Property(x => x.ScopeDimension).HasConversion<int?>();
        builder.Property(x => x.ScopeValueDisplay).HasMaxLength(250);
        builder.Property(x => x.Action).HasMaxLength(80).IsRequired();
        builder.Property(x => x.OldValue).HasMaxLength(1000);
        builder.Property(x => x.NewValue).HasMaxLength(1000);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.OccurredAtUtc, x.Id }).HasDatabaseName("IX_AuthConfigEvent_Tenant_Occurred_Id");
        builder.HasIndex(x => new { x.TenantId, x.RoleId, x.OccurredAtUtc }).HasDatabaseName("IX_AuthConfigEvent_Tenant_Role_Occurred");
        builder.HasIndex(x => new { x.TenantId, x.UserRoleAssignmentId, x.OccurredAtUtc }).HasDatabaseName("IX_AuthConfigEvent_Tenant_Assignment_Occurred");
        builder.HasIndex(x => new { x.TenantId, x.ActorUserId, x.OccurredAtUtc }).HasDatabaseName("IX_AuthConfigEvent_Tenant_Actor_Occurred");
    }
}
