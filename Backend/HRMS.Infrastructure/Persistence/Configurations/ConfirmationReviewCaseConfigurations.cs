using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class ConfirmationReviewCaseConfiguration : IEntityTypeConfiguration<ConfirmationReviewCase>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewCase> b)
    {
        b.ToTable("ConfirmationReviewCases"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.EmployeeId).IsRequired();
        b.Property(x => x.ConfirmationPolicyId).IsRequired();
        b.Property(x => x.ConfirmationPolicyVersionId).IsRequired();
        b.Property(x => x.ProbationCycleKey).HasMaxLength(100).IsRequired();
        b.Property(x => x.ReviewRound).HasDefaultValue(1).IsRequired();
        b.Property(x => x.ProbationStartDate).HasColumnType("date");
        b.Property(x => x.ProbationEndDate).HasColumnType("date");
        b.Property(x => x.DueDate).HasColumnType("date");
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(256);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        // One active case per employee per probation cycle; the service is idempotent against this.
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.ProbationCycleKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status });

        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ConfirmationPolicy).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationPolicyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ManagerReviewer).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ManagerReviewerEmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.HrbpReviewer).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HrbpReviewerEmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConfirmationReviewCaseQuestionConfiguration : IEntityTypeConfiguration<ConfirmationReviewCaseQuestion>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewCaseQuestion> b)
    {
        b.ToTable("ConfirmationReviewCaseQuestions"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.SourceQuestionId).IsRequired();
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.QuestionText).HasMaxLength(500).IsRequired();
        b.Property(x => x.HelpText).HasMaxLength(1000);
        b.Property(x => x.QuestionType).HasConversion<int>().IsRequired();
        b.Property(x => x.RatingScaleCode).HasMaxLength(40);
        b.Property(x => x.RatingScaleName).HasMaxLength(150);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewCaseId });
        b.HasOne(x => x.ConfirmationReviewCase).WithMany(x => x.Questions)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewCaseId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfirmationReviewCaseQuestionOptionConfiguration : IEntityTypeConfiguration<ConfirmationReviewCaseQuestionOption>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewCaseQuestionOption> b)
    {
        b.ToTable("ConfirmationReviewCaseQuestionOptions"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.Code).HasMaxLength(60).IsRequired();
        b.Property(x => x.Label).HasMaxLength(200).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewCaseQuestionId });
        b.HasOne(x => x.ConfirmationReviewCaseQuestion).WithMany(x => x.Options)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewCaseQuestionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfirmationReviewCaseRatingValueConfiguration : IEntityTypeConfiguration<ConfirmationReviewCaseRatingValue>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewCaseRatingValue> b)
    {
        b.ToTable("ConfirmationReviewCaseRatingValues"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.RatingValue).IsRequired();
        b.Property(x => x.Label).HasMaxLength(150).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewCaseQuestionId });
        b.HasOne(x => x.ConfirmationReviewCaseQuestion).WithMany(x => x.RatingValues)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewCaseQuestionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfirmationReviewerReviewConfiguration : IEntityTypeConfiguration<ConfirmationReviewerReview>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewerReview> b)
    {
        b.ToTable("ConfirmationReviewerReviews"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.ReviewerType).HasConversion<int>().IsRequired();
        b.Property(x => x.ReviewRound).HasDefaultValue(1).IsRequired();
        b.Property(x => x.VersionNumber).IsRequired();
        b.Property(x => x.Status).HasConversion<int>().IsRequired();
        b.Property(x => x.OverallComments).HasMaxLength(4000);
        b.Property(x => x.SubmittedBy).HasMaxLength(256);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewCaseId, x.ReviewRound, x.ReviewerType, x.VersionNumber }).IsUnique();
        b.HasOne(x => x.ConfirmationReviewCase).WithMany(x => x.Reviews)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewCaseId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfirmationReviewResponseConfiguration : IEntityTypeConfiguration<ConfirmationReviewResponse>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewResponse> b)
    {
        b.ToTable("ConfirmationReviewResponses"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.TextValue).HasMaxLength(4000);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewerReviewId });
        b.HasOne(x => x.ConfirmationReviewerReview).WithMany(x => x.Responses)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewerReviewId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        // Restrict (not cascade) so the snapshot question is not a second cascade path from the case.
        b.HasOne(x => x.ConfirmationReviewCaseQuestion).WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewCaseQuestionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConfirmationReviewResponseOptionConfiguration : IEntityTypeConfiguration<ConfirmationReviewResponseOption>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewResponseOption> b)
    {
        b.ToTable("ConfirmationReviewResponseOptions"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.OptionCode).HasMaxLength(60).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewResponseId });
        b.HasOne(x => x.ConfirmationReviewResponse).WithMany(x => x.SelectedOptions)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewResponseId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConfirmationReviewCaseEventConfiguration : IEntityTypeConfiguration<ConfirmationReviewCaseEvent>
{
    public void Configure(EntityTypeBuilder<ConfirmationReviewCaseEvent> b)
    {
        b.ToTable("ConfirmationReviewCaseEvents"); b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.Property(x => x.EventType).HasConversion<int>().IsRequired();
        b.Property(x => x.Details).HasMaxLength(4000);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.ConfirmationReviewCaseId });
        b.HasOne(x => x.ConfirmationReviewCase).WithMany(x => x.Events)
            .HasForeignKey(x => new { x.TenantId, x.ConfirmationReviewCaseId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
