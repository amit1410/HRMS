using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class ConfirmationPolicyConfiguration : IEntityTypeConfiguration<ConfirmationPolicy>
{
    public void Configure(EntityTypeBuilder<ConfirmationPolicy> b)
    {
        b.ToTable("ConfirmationPolicies"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.IsActive).HasDefaultValue(true); b.Property(x => x.CreatedBy).HasMaxLength(256); b.Property(x => x.ModifiedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConfirmationPolicyVersionConfiguration : IEntityTypeConfiguration<ConfirmationPolicyVersion>
{
    public void Configure(EntityTypeBuilder<ConfirmationPolicyVersion> b)
    {
        b.ToTable("ConfirmationPolicyVersions"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.VersionNumber).IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnType("date").IsRequired(); b.Property(x => x.EffectiveTo).HasColumnType("date");
        b.Property(x => x.Status).HasConversion<int>().IsRequired(); b.Property(x => x.Priority).IsRequired(); b.Property(x => x.CreatedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.ConfirmationPolicyId, x.VersionNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status, x.EffectiveFrom, x.EffectiveTo });
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.ConfirmationPolicy).WithMany(x => x.Versions).HasForeignKey(x => new { x.TenantId, x.ConfirmationPolicyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConfirmationPolicyRuleConfiguration : IEntityTypeConfiguration<ConfirmationPolicyRule>
{
    public void Configure(EntityTypeBuilder<ConfirmationPolicyRule> b)
    {
        b.ToTable("ConfirmationPolicyRules"); b.HasKey(x => x.Id); b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ProbationDuration).IsRequired(); b.Property(x => x.ProbationDurationUnit).HasConversion<int>().IsRequired();
        b.Property(x => x.TriggerDaysBeforeEnd).IsRequired(); b.Property(x => x.MaxExtensions).IsRequired();
        b.Property(x => x.ExtensionDuration).IsRequired(); b.Property(x => x.ExtensionDurationUnit).HasConversion<int>().IsRequired();
        b.Property(x => x.RequiredApproval).HasConversion<int>().IsRequired(); b.Property(x => x.ConfirmRatingThreshold).HasPrecision(9, 3); b.Property(x => x.AutoConfirmOnApproval).IsRequired();
        b.Property(x => x.OverallRatingMethod).HasConversion<int>().HasDefaultValue(HRMS.Domain.Enums.OverallRatingMethod.AverageOfRatings).IsRequired();
        b.Property(x => x.ApprovalSequence).HasConversion<int>().HasDefaultValue(HRMS.Domain.Enums.ApprovalSequence.Parallel).IsRequired();
        b.Property(x => x.RequireReviewerComments).HasDefaultValue(false).IsRequired();
        b.Property(x => x.ConfirmedJobStatus).HasMaxLength(100); b.Property(x => x.NotConfirmedJobStatus).HasMaxLength(100);
        b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.HasIndex(x => new { x.TenantId, x.ConfirmationPolicyVersionId }).IsUnique();
        b.HasOne(x => x.ConfirmationPolicyVersion).WithOne(x => x.Rule)
            .HasForeignKey<ConfirmationPolicyRule>(x => new { x.TenantId, x.ConfirmationPolicyVersionId })
            .HasPrincipalKey<ConfirmationPolicyVersion>(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConfirmationPolicyApplicabilitySetConfiguration : IEntityTypeConfiguration<ConfirmationPolicyApplicabilitySet>
{
    public void Configure(EntityTypeBuilder<ConfirmationPolicyApplicabilitySet> b)
    {
        b.ToTable("ConfirmationPolicyApplicabilitySets"); b.HasKey(x => x.Id); b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.Gender).HasConversion<int>();
        b.HasIndex(x => new { x.TenantId, x.ConfirmationPolicyVersionId }); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.ConfirmationPolicyVersion).WithMany(x => x.ApplicabilitySets).HasForeignKey(x => new { x.TenantId, x.ConfirmationPolicyVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.HoldingCompany).WithMany().HasForeignKey(x => new { x.TenantId, x.HoldingCompanyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Lob).WithMany().HasForeignKey(x => new { x.TenantId, x.LobId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Organisation).WithMany().HasForeignKey(x => new { x.TenantId, x.OrganisationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Department).WithMany().HasForeignKey(x => new { x.TenantId, x.DepartmentId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SubDepartment).WithMany().HasForeignKey(x => new { x.TenantId, x.SubDepartmentId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Section).WithMany().HasForeignKey(x => new { x.TenantId, x.SectionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SubSection).WithMany().HasForeignKey(x => new { x.TenantId, x.SubSectionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Function).WithMany().HasForeignKey(x => new { x.TenantId, x.FunctionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.SubFunction).WithMany().HasForeignKey(x => new { x.TenantId, x.SubFunctionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Grade).WithMany().HasForeignKey(x => new { x.TenantId, x.GradeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Designation).WithMany().HasForeignKey(x => new { x.TenantId, x.DesignationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EmployeeType).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeTypeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        // Country is an existing global reference master, so it intentionally has no TenantId component.
        b.HasOne(x => x.CountryLocation).WithMany().HasForeignKey(x => x.CountryLocationId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.WorkLocation).WithMany().HasForeignKey(x => new { x.TenantId, x.WorkLocationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CostCenter).WithMany().HasForeignKey(x => new { x.TenantId, x.CostCenterId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
