using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class ConfirmationDecisionRuleConfiguration : IEntityTypeConfiguration<ConfirmationDecisionRule>
{
    public void Configure(EntityTypeBuilder<ConfirmationDecisionRule> b)
    {
        b.ToTable("ConfirmationDecisionRules"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Priority).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.MinOverallRating).HasPrecision(9, 3);
        b.Property(x => x.MaxOverallRating).HasPrecision(9, 3);
        b.Property(x => x.Outcome).HasConversion<int>().IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationPolicyVersionId, x.Priority });
        b.HasOne(x => x.ConfirmationPolicyVersion).WithMany(x => x.DecisionRules)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationPolicyVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfirmationDecisionCalculationConfiguration : IEntityTypeConfiguration<ConfirmationDecisionCalculation>
{
    public void Configure(EntityTypeBuilder<ConfirmationDecisionCalculation> b)
    {
        b.ToTable("ConfirmationDecisionCalculations"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.OverallRating).HasPrecision(9, 3);
        b.Property(x => x.ReviewRound).HasDefaultValue(1).IsRequired();
        b.Property(x => x.CalculatedOutcome).HasConversion<int>().IsRequired();
        b.Property(x => x.Explanation).HasMaxLength(4000);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewCaseId });
        b.HasOne(x => x.ConfirmationReviewCase).WithMany(x => x.Calculations)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewCaseId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfirmationDecisionConfiguration : IEntityTypeConfiguration<ConfirmationDecision>
{
    public void Configure(EntityTypeBuilder<ConfirmationDecision> b)
    {
        b.ToTable("ConfirmationDecisions"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ProbationCycleKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.ReviewRound).HasDefaultValue(1).IsRequired();
        b.Property(x => x.Outcome).HasConversion<int>().IsRequired();
        b.Property(x => x.DecisionReason).HasMaxLength(4000);
        b.Property(x => x.CalculatedOverallRating).HasPrecision(9, 3);
        b.Property(x => x.PreviousCaseStatus).HasConversion<int>().IsRequired();
        b.Property(x => x.NewCaseStatus).HasConversion<int>().IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewCaseId });
        b.HasOne(x => x.ConfirmationReviewCase).WithMany(x => x.Decisions)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewCaseId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfirmationProbationCycleConfiguration : IEntityTypeConfiguration<ConfirmationProbationCycle>
{
    public void Configure(EntityTypeBuilder<ConfirmationProbationCycle> b)
    {
        b.ToTable("ConfirmationProbationCycles"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.EmployeeId).IsRequired();
        b.Property(x => x.ConfirmationPolicyId).IsRequired();
        b.Property(x => x.ConfirmationPolicyVersionId).IsRequired();
        b.Property(x => x.CycleNumber).IsRequired();
        b.Property(x => x.StartDate).HasColumnType("date");
        b.Property(x => x.BaselineEndDate).HasColumnType("date");
        b.Property(x => x.CurrentEndDate).HasColumnType("date");
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.ExtensionCount).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        // Fast "the employee's active cycle" lookup used by the resolver and the decision service.
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.CycleNumber }).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConfirmationProbationExtensionConfiguration : IEntityTypeConfiguration<ConfirmationProbationExtension>
{
    public void Configure(EntityTypeBuilder<ConfirmationProbationExtension> b)
    {
        b.ToTable("ConfirmationProbationExtensions"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.EmployeeId).IsRequired();
        b.Property(x => x.PreviousEndDate).HasColumnType("date");
        b.Property(x => x.NewEndDate).HasColumnType("date");
        b.Property(x => x.ExtensionDuration).IsRequired();
        b.Property(x => x.ExtensionDurationUnit).HasConversion<int>().IsRequired();
        b.Property(x => x.Reason).HasMaxLength(4000);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationProbationCycleId });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewCaseId });
        b.HasOne(x => x.ConfirmationProbationCycle).WithMany(x => x.Extensions)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationProbationCycleId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
