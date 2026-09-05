using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class LeaveTypeConfiguration : IEntityTypeConfiguration<LeaveType>
{
    public void Configure(EntityTypeBuilder<LeaveType> b)
    {
        b.ToTable("LeaveTypes"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.DefaultUnit).HasConversion<int>().IsRequired(); b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.CreatedBy).HasMaxLength(256); b.Property(x => x.ModifiedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePeriodConfiguration : IEntityTypeConfiguration<LeavePeriod>
{
    public void Configure(EntityTypeBuilder<LeavePeriod> b)
    {
        b.ToTable("LeavePeriods"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.Property(x => x.StartDate).HasColumnType("date").IsRequired();
        b.Property(x => x.EndDate).HasColumnType("date").IsRequired(); b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.CreatedBy).HasMaxLength(256); b.Property(x => x.ModifiedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyConfiguration : IEntityTypeConfiguration<LeavePolicy>
{
    public void Configure(EntityTypeBuilder<LeavePolicy> b)
    {
        b.ToTable("LeavePolicies"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired(); b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.IsActive).HasDefaultValue(true); b.Property(x => x.CreatedBy).HasMaxLength(256); b.Property(x => x.ModifiedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyVersionConfiguration : IEntityTypeConfiguration<LeavePolicyVersion>
{
    public void Configure(EntityTypeBuilder<LeavePolicyVersion> b)
    {
        b.ToTable("LeavePolicyVersions"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.VersionNumber).IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnType("date").IsRequired(); b.Property(x => x.EffectiveTo).HasColumnType("date");
        b.Property(x => x.Status).HasConversion<int>().IsRequired(); b.Property(x => x.Priority).IsRequired(); b.Property(x => x.CreatedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.LeavePolicyId, x.VersionNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status, x.EffectiveFrom, x.EffectiveTo });
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.LeavePolicy).WithMany(x => x.Versions).HasForeignKey(x => new { x.TenantId, x.LeavePolicyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyRuleConfiguration : IEntityTypeConfiguration<LeavePolicyRule>
{
    public void Configure(EntityTypeBuilder<LeavePolicyRule> b)
    {
        b.ToTable("LeavePolicyRules"); b.HasKey(x => x.Id); b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasIndex(x => new { x.TenantId, x.LeavePolicyVersionId, x.LeaveTypeId }).IsUnique();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasAlternateKey(x => new { x.TenantId, x.LeavePolicyVersionId, x.Id });
        b.HasOne(x => x.LeavePolicyVersion).WithMany(x => x.Rules).HasForeignKey(x => new { x.TenantId, x.LeavePolicyVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LeaveType).WithMany(x => x.PolicyRules).HasForeignKey(x => new { x.TenantId, x.LeaveTypeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyEligibilityRuleConfiguration : IEntityTypeConfiguration<LeavePolicyEligibilityRule>
{
    public void Configure(EntityTypeBuilder<LeavePolicyEligibilityRule> b)
    {
        b.ToTable("LeavePolicyEligibilityRules");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.EligibilityMode).HasConversion<int>().IsRequired();
        b.Property(x => x.MinimumServiceUnit).HasConversion<int>();
        b.Property(x => x.ProbationMode).HasConversion<int>().IsRequired();
        b.Property(x => x.NoticePeriodMode).HasConversion<int>().IsRequired();
        b.HasIndex(x => new { x.TenantId, x.LeavePolicyRuleId }).IsUnique();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.LeavePolicyRule).WithOne(x => x.EligibilityRule)
            .HasForeignKey<LeavePolicyEligibilityRule>(x => new { x.TenantId, x.LeavePolicyRuleId })
            .HasPrincipalKey<LeavePolicyRule>(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyEntitlementRuleConfiguration : IEntityTypeConfiguration<LeavePolicyEntitlementRule>
{
    public void Configure(EntityTypeBuilder<LeavePolicyEntitlementRule> b)
    {
        b.ToTable("LeavePolicyEntitlementRules");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.EntitlementMode).HasConversion<int>().IsRequired();
        b.Property(x => x.EntitlementSource).HasConversion<int>().IsRequired();
        b.Property(x => x.EntitlementQuantity).HasPrecision(9, 3);
        b.Property(x => x.AccrualFrequency).HasConversion<int>().IsRequired();
        b.Property(x => x.AccrualTiming).HasConversion<int>();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.LeavePolicyRuleId }).IsUnique();
        b.HasOne(x => x.LeavePolicyRule).WithOne(x => x.EntitlementRule)
            .HasForeignKey<LeavePolicyEntitlementRule>(x => new { x.TenantId, x.LeavePolicyRuleId })
            .HasPrincipalKey<LeavePolicyRule>(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyRequestRuleConfiguration : IEntityTypeConfiguration<LeavePolicyRequestRule>
{
    public void Configure(EntityTypeBuilder<LeavePolicyRequestRule> b)
    {
        b.ToTable("LeavePolicyRequestRules"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.MinimumRequestQuantity).HasPrecision(9, 3);
        b.Property(x => x.MaximumRequestQuantity).HasPrecision(9, 3);
        b.Property(x => x.MaximumConsecutiveQuantity).HasPrecision(9, 3);
        b.Property(x => x.MaximumQuantityPerPeriod).HasPrecision(9, 3);
        b.Property(x => x.BackdatedRequestMode).HasConversion<int>().IsRequired();
        b.Property(x => x.RequestLimitPeriod).HasConversion<int>();
        b.Property(x => x.PartialDayMode).HasConversion<int>().IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.LeavePolicyRuleId }).IsUnique();
        b.HasOne(x => x.LeavePolicyRule).WithOne(x => x.RequestRule)
            .HasForeignKey<LeavePolicyRequestRule>(x => new { x.TenantId, x.LeavePolicyRuleId })
            .HasPrincipalKey<LeavePolicyRule>(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyCalendarRuleConfiguration : IEntityTypeConfiguration<LeavePolicyCalendarRule>
{
    public void Configure(EntityTypeBuilder<LeavePolicyCalendarRule> b)
    {
        b.ToTable("LeavePolicyCalendarRules"); b.HasKey(x => x.Id); b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.HolidayTreatment).HasConversion<int>().IsRequired(); b.Property(x => x.WeekOffTreatment).HasConversion<int>().IsRequired(); b.Property(x => x.SandwichMode).HasConversion<int>().IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.HasIndex(x => new { x.TenantId, x.LeavePolicyRuleId }).IsUnique();
        b.HasOne(x => x.LeavePolicyRule).WithOne(x => x.CalendarRule).HasForeignKey<LeavePolicyCalendarRule>(x => new { x.TenantId, x.LeavePolicyRuleId }).HasPrincipalKey<LeavePolicyRule>(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyAttachmentRuleConfiguration : IEntityTypeConfiguration<LeavePolicyAttachmentRule>
{
    public void Configure(EntityTypeBuilder<LeavePolicyAttachmentRule> b)
    {
        b.ToTable("LeavePolicyAttachmentRules"); b.HasKey(x => x.Id); b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.AttachmentRequirement).HasConversion<int>().IsRequired(); b.Property(x => x.ThresholdQuantity).HasPrecision(9, 3); b.Property(x => x.DocumentLabel).HasMaxLength(200);
        b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.HasIndex(x => new { x.TenantId, x.LeavePolicyRuleId }).IsUnique();
        b.HasOne(x => x.LeavePolicyRule).WithOne(x => x.AttachmentRule).HasForeignKey<LeavePolicyAttachmentRule>(x => new { x.TenantId, x.LeavePolicyRuleId }).HasPrincipalKey<LeavePolicyRule>(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyClubbingRuleConfiguration : IEntityTypeConfiguration<LeavePolicyClubbingRule>
{
    public void Configure(EntityTypeBuilder<LeavePolicyClubbingRule> b)
    {
        b.ToTable("LeavePolicyClubbingRules"); b.HasKey(x => x.Id); b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.Relation).HasConversion<int>().IsRequired();
        b.Property<string>("NormalizedPairKey").HasComputedColumnSql("CASE WHEN CONVERT(varchar(36), [LowerLeavePolicyRuleId]) < CONVERT(varchar(36), [HigherLeavePolicyRuleId]) THEN CONVERT(varchar(36), [LowerLeavePolicyRuleId]) + ':' + CONVERT(varchar(36), [HigherLeavePolicyRuleId]) ELSE CONVERT(varchar(36), [HigherLeavePolicyRuleId]) + ':' + CONVERT(varchar(36), [LowerLeavePolicyRuleId]) END", stored: true).HasMaxLength(73);
        b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.HasIndex("TenantId", "LeavePolicyVersionId", "NormalizedPairKey").IsUnique().HasFilter(null);
        b.ToTable("LeavePolicyClubbingRules", t => t.HasCheckConstraint("CK_LeavePolicyClubbingRules_DifferentParticipants", "[LowerLeavePolicyRuleId] <> [HigherLeavePolicyRuleId]"));
        b.HasOne(x => x.LeavePolicyVersion).WithMany().HasForeignKey(x => new { x.TenantId, x.LeavePolicyVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.LowerLeavePolicyRule).WithMany(x => x.ClubbingRulesAsLower).HasForeignKey(x => new { x.TenantId, x.LeavePolicyVersionId, x.LowerLeavePolicyRuleId }).HasPrincipalKey(x => new { x.TenantId, x.LeavePolicyVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.HigherLeavePolicyRule).WithMany(x => x.ClubbingRulesAsHigher).HasForeignKey(x => new { x.TenantId, x.LeavePolicyVersionId, x.HigherLeavePolicyRuleId }).HasPrincipalKey(x => new { x.TenantId, x.LeavePolicyVersionId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyCancellationRuleConfiguration : IEntityTypeConfiguration<LeavePolicyCancellationRule>
{
    public void Configure(EntityTypeBuilder<LeavePolicyCancellationRule> b)
    {
        b.ToTable("LeavePolicyCancellationRules"); b.HasKey(x => x.Id); b.Property(x => x.TenantId).IsRequired(); b.HasAlternateKey(x => new { x.TenantId, x.Id }); b.HasIndex(x => new { x.TenantId, x.LeavePolicyRuleId }).IsUnique();
        b.HasOne(x => x.LeavePolicyRule).WithOne(x => x.CancellationRule).HasForeignKey<LeavePolicyCancellationRule>(x => new { x.TenantId, x.LeavePolicyRuleId }).HasPrincipalKey<LeavePolicyRule>(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LeavePolicyApplicabilitySetConfiguration : IEntityTypeConfiguration<LeavePolicyApplicabilitySet>
{
    public void Configure(EntityTypeBuilder<LeavePolicyApplicabilitySet> b)
    {
        b.ToTable("LeavePolicyApplicabilitySets"); b.HasKey(x => x.Id); b.Property(x => x.TenantId).IsRequired(); b.Property(x => x.Gender).HasConversion<int>();
        b.HasIndex(x => new { x.TenantId, x.LeavePolicyVersionId }); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.LeavePolicyVersion).WithMany(x => x.ApplicabilitySets).HasForeignKey(x => new { x.TenantId, x.LeavePolicyVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
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
