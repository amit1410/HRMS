using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");

        // A user holds a given role once; composite key prevents duplicate assignments.
        builder.HasKey(ur => new { ur.UserId, ur.RoleId });

        builder.Property(ur => ur.TenantId).IsRequired();
        builder.HasIndex(ur => ur.TenantId);

        builder.HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // The User -> UserRoles side (cascade) is configured on UserConfiguration.
    }
}
