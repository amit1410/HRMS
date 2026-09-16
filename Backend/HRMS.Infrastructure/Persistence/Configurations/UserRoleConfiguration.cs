using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");

        builder.HasKey(ur => ur.Id);
        builder.Property(ur => ur.Id).ValueGeneratedNever();

        builder.Property(ur => ur.TenantId).IsRequired();
        builder.HasIndex(ur => ur.TenantId);
        builder.HasIndex(ur => new { ur.TenantId, ur.UserId, ur.RoleId, ur.EffectiveFrom })
            .HasDatabaseName("IX_UserRole_Effective");
        builder.Property(ur => ur.AssignmentReason).HasMaxLength(500);
        builder.Property(ur => ur.AssignmentSource).HasConversion<int>();

        builder.HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(ur => ur.Events)
            .WithOne(e => e.Assignment)
            .HasForeignKey(e => e.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(ur => ur.Scopes)
            .WithOne(s => s.Assignment)
            .HasForeignKey(s => s.UserRoleAssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // The User -> UserRoles side (cascade) is configured on UserConfiguration.
    }
}
