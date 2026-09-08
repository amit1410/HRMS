using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class PasswordResetOtpConfiguration : IEntityTypeConfiguration<PasswordResetOtp>
{
    public void Configure(EntityTypeBuilder<PasswordResetOtp> builder)
    {
        builder.ToTable("PasswordResetOtps");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Channel).HasConversion<int>().IsRequired();
        builder.Property(x => x.Purpose).HasConversion<int>().IsRequired();
        builder.Property(x => x.ChallengeIdHash).IsRequired().HasMaxLength(64);
        builder.Property(x => x.OtpHash).IsRequired().HasMaxLength(500);
        builder.Property(x => x.DestinationHash).HasMaxLength(64);
        builder.Property(x => x.MaskedDestination).IsRequired().HasMaxLength(256);
        builder.Property(x => x.ExpiresAtUtc).IsRequired();
        builder.Property(x => x.LastSentAtUtc).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.ChallengeIdHash, x.Channel }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.RevokedAtUtc, x.ConsumedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.ExpiresAtUtc });
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.UserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
