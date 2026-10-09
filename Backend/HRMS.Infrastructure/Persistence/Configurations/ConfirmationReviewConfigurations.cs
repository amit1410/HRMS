using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class ConfirmationRatingScaleConfiguration : IEntityTypeConfiguration<ConfirmationRatingScale>
{
    public void Configure(EntityTypeBuilder<ConfirmationRatingScale> b)
    {
        b.ToTable("ConfirmationRatingScales"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.MinRating).IsRequired();
        b.Property(x => x.MaxRating).IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.DisplayOrder).IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnType("date");
        b.Property(x => x.EffectiveTo).HasColumnType("date");
        b.Property(x => x.CreatedBy).HasMaxLength(256);
        b.Property(x => x.ModifiedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConfirmationRatingScaleValueConfiguration : IEntityTypeConfiguration<ConfirmationRatingScaleValue>
{
    public void Configure(EntityTypeBuilder<ConfirmationRatingScaleValue> b)
    {
        b.ToTable("ConfirmationRatingScaleValues"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.RatingValue).IsRequired();
        b.Property(x => x.Label).HasMaxLength(150).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.DisplayOrder).IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationRatingScaleId });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationRatingScaleId, x.RatingValue }).IsUnique();
        b.HasOne(x => x.ConfirmationRatingScale).WithMany(x => x.Values)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationRatingScaleId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfirmationReviewQuestionConfiguration : IEntityTypeConfiguration<ConfirmationReviewQuestion>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewQuestion> b)
    {
        b.ToTable("ConfirmationReviewQuestions"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.QuestionText).HasMaxLength(500).IsRequired();
        b.Property(x => x.HelpText).HasMaxLength(1000);
        b.Property(x => x.QuestionType).HasConversion<int>().IsRequired();
        b.Property(x => x.IsMandatory).IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.Property(x => x.DisplayOrder).IsRequired();
        b.Property(x => x.EffectiveFrom).HasColumnType("date");
        b.Property(x => x.EffectiveTo).HasColumnType("date");
        b.Property(x => x.CreatedBy).HasMaxLength(256);
        b.Property(x => x.ModifiedBy).HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ConfirmationRatingScale).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationRatingScaleId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConfirmationReviewQuestionOptionConfiguration : IEntityTypeConfiguration<ConfirmationReviewQuestionOption>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewQuestionOption> b)
    {
        b.ToTable("ConfirmationReviewQuestionOptions"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Code).HasMaxLength(60).IsRequired();
        b.Property(x => x.Label).HasMaxLength(200).IsRequired();
        b.Property(x => x.DisplayOrder).IsRequired();
        b.Property(x => x.IsActive).HasDefaultValue(true);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewQuestionId });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewQuestionId, x.Code }).IsUnique();
        b.HasOne(x => x.ConfirmationReviewQuestion).WithMany(x => x.Options)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewQuestionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfirmationReviewQuestionApplicabilitySetConfiguration : IEntityTypeConfiguration<ConfirmationReviewQuestionApplicabilitySet>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewQuestionApplicabilitySet> b)
    {
        b.ToTable("ConfirmationReviewQuestionApplicabilitySets"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Gender).HasConversion<int>();
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewQuestionId });
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne(x => x.ConfirmationReviewQuestion).WithMany(x => x.ApplicabilitySets)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewQuestionId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ConfirmationPolicy).WithMany().HasForeignKey(x => new { x.TenantId, x.ConfirmationPolicyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
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
