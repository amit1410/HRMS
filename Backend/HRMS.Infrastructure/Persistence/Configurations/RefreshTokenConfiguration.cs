using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.TenantId).IsRequired();
        builder.Property(t => t.UserId).IsRequired();

        // Uppercase hex of a SHA-256 digest: always 64 characters.
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);

        builder.Property(t => t.ExpiresAtUtc).IsRequired();

        // Refresh lookups happen by hash before any tenant is known, so this index carries the whole
        // lookup. Unique because a hash collision would mean two sessions share one credential.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Supports "revoke every active session for this user" during replay handling and sign-out.
        builder.HasIndex(t => new { t.TenantId, t.UserId });

        builder.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
