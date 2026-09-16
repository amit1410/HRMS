using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class UserRoleAssignmentEventConfiguration : IEntityTypeConfiguration<UserRoleAssignmentEvent>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignmentEvent> builder)
    {
        builder.ToTable("UserRoleAssignmentEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EventType).HasConversion<int>();
        builder.Property(x => x.AssignmentSource).HasConversion<int>();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.HasIndex(x => new { x.TenantId, x.AssignmentId, x.OccurredAtUtc, x.Id })
            .HasDatabaseName("IX_URAEvent_Tenant_Assignment_Occurred_Id");
        builder.HasOne(x => x.Assignment).WithMany(x => x.Events)
            .HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class UserRoleAssignmentScopeConfiguration : IEntityTypeConfiguration<UserRoleAssignmentScope>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignmentScope> builder)
    {
        builder.ToTable("UserRoleAssignmentScopes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ScopeType).HasConversion<int>();
        builder.HasIndex(x => new { x.TenantId, x.UserRoleAssignmentId, x.ScopeType, x.ScopeEntityId })
            .HasDatabaseName("UX_URAScope_Assignment_Type_Entity").IsUnique();
        builder.HasOne(x => x.Assignment).WithMany(x => x.Scopes)
            .HasForeignKey(x => x.UserRoleAssignmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
