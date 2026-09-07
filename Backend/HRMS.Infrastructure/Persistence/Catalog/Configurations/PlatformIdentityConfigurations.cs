using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Catalog.Configurations;

public sealed class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.ToTable("PlatformUsers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Email).IsRequired().HasMaxLength(256);
        builder.Property(x => x.NormalizedEmail).IsRequired().HasMaxLength(256);
        builder.Property(x => x.PasswordHash).IsRequired().HasMaxLength(512);
        builder.Property(x => x.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(x => x.LastName).IsRequired().HasMaxLength(100);
        builder.Property(x => x.SecurityRevision).IsRequired();
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();
        builder.HasMany(x => x.UserRoles).WithOne(x => x.PlatformUser)
            .HasForeignKey(x => x.PlatformUserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.RefreshTokens).WithOne(x => x.PlatformUser)
            .HasForeignKey(x => x.PlatformUserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PlatformRoleConfiguration : IEntityTypeConfiguration<PlatformRole>
{
    public void Configure(EntityTypeBuilder<PlatformRole> builder)
    {
        builder.ToTable("PlatformRoles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class PlatformPermissionConfiguration : IEntityTypeConfiguration<PlatformPermission>
{
    public void Configure(EntityTypeBuilder<PlatformPermission> builder)
    {
        builder.ToTable("PlatformPermissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).IsRequired().HasMaxLength(100);
        builder.HasIndex(x => x.Name).IsUnique();
    }
}

public sealed class PlatformUserRoleConfiguration : IEntityTypeConfiguration<PlatformUserRole>
{
    public void Configure(EntityTypeBuilder<PlatformUserRole> builder)
    {
        builder.ToTable("PlatformUserRoles");
        builder.HasKey(x => new { x.PlatformUserId, x.PlatformRoleId });
        builder.HasOne(x => x.PlatformRole).WithMany(x => x.UserRoles)
            .HasForeignKey(x => x.PlatformRoleId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PlatformRolePermissionConfiguration : IEntityTypeConfiguration<PlatformRolePermission>
{
    public void Configure(EntityTypeBuilder<PlatformRolePermission> builder)
    {
        builder.ToTable("PlatformRolePermissions");
        builder.HasKey(x => new { x.PlatformRoleId, x.PlatformPermissionId });
        builder.HasOne(x => x.PlatformPermission).WithMany(x => x.RolePermissions)
            .HasForeignKey(x => x.PlatformPermissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PlatformRefreshTokenConfiguration : IEntityTypeConfiguration<PlatformRefreshToken>
{
    public void Configure(EntityTypeBuilder<PlatformRefreshToken> builder)
    {
        builder.ToTable("PlatformRefreshTokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(128);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.PlatformUserId, x.RevokedAtUtc });
        builder.HasOne(x => x.PlatformUser).WithMany(x => x.RefreshTokens)
            .HasForeignKey(x => x.PlatformUserId).OnDelete(DeleteBehavior.Cascade);
    }
}
