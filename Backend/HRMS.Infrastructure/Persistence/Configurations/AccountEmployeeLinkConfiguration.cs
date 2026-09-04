using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class AccountEmployeeCurrentLinkConfiguration : IEntityTypeConfiguration<AccountEmployeeCurrentLink>
{
    public void Configure(EntityTypeBuilder<AccountEmployeeCurrentLink> builder)
    {
        builder.ToTable("AccountEmployeeCurrentLinks");
        builder.HasKey(x => x.LinkId);
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.EmployeeId).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasDatabaseName("UX_AccountEmployeeCurrentLinks_TenantId_UserId");
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId }).IsUnique().HasDatabaseName("UX_AccountEmployeeCurrentLinks_TenantId_EmployeeId");
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.User).WithMany().HasForeignKey(x => new { x.TenantId, x.UserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreationEvent).WithOne().HasForeignKey<AccountEmployeeCurrentLink>(x => new { x.TenantId, x.UserId, x.LinkId }).HasPrincipalKey<AccountEmployeeLinkEvent>(x => new { x.TenantId, x.SubjectUserId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AccountEmployeeLinkEventConfiguration : IEntityTypeConfiguration<AccountEmployeeLinkEvent>
{
    public void Configure(EntityTypeBuilder<AccountEmployeeLinkEvent> builder)
    {
        builder.ToTable("AccountEmployeeLinkEvents", t => t.HasCheckConstraint("CK_AccountEmployeeLinkEvents_Shape", "[Sequence] > 0 AND [Operation] IN ('Link','Unlink','Replace') AND [Reason] <> '' AND [CorrelationId] <> '' AND (([Operation] = 'Link' AND [PreviousLinkId] IS NULL AND [BeforeEmployeeId] IS NULL AND [NewLinkId] = [Id] AND [AfterEmployeeId] IS NOT NULL) OR ([Operation] = 'Unlink' AND [PreviousLinkId] IS NOT NULL AND [BeforeEmployeeId] IS NOT NULL AND [NewLinkId] IS NULL AND [AfterEmployeeId] IS NULL) OR ([Operation] = 'Replace' AND [PreviousLinkId] IS NOT NULL AND [BeforeEmployeeId] IS NOT NULL AND [NewLinkId] = [Id] AND [AfterEmployeeId] IS NOT NULL AND [BeforeEmployeeId] <> [AfterEmployeeId]))"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Operation).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.OccurredAtUtc).HasConversion<DateTime>();
        builder.HasAlternateKey(x => new { x.TenantId, x.SubjectUserId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.SubjectUserId, x.Sequence }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.BeforeEmployeeId, x.OccurredAtUtc, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.AfterEmployeeId, x.OccurredAtUtc, x.Id });
        builder.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SubjectUser).WithMany().HasForeignKey(x => new { x.TenantId, x.SubjectUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => new { x.TenantId, x.ActorUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.BeforeEmployee).WithMany().HasForeignKey(x => new { x.TenantId, x.BeforeEmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AfterEmployee).WithMany().HasForeignKey(x => new { x.TenantId, x.AfterEmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PreviousEvent).WithMany().HasForeignKey(x => new { x.TenantId, x.SubjectUserId, x.PreviousEventId }).HasPrincipalKey(x => new { x.TenantId, x.SubjectUserId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PreviousLink).WithMany().HasForeignKey(x => new { x.TenantId, x.SubjectUserId, x.PreviousLinkId }).HasPrincipalKey(x => new { x.TenantId, x.SubjectUserId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
