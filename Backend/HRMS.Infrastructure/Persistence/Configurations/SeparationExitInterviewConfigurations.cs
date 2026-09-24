using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class SeparationExitInterviewTemplateConfiguration : IEntityTypeConfiguration<SeparationExitInterviewTemplate>
{
    public void Configure(EntityTypeBuilder<SeparationExitInterviewTemplate> b)
    {
        b.ToTable("SeparationExitInterviewTemplates"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b);
        b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(4000); b.Property(x => x.AppliesToSeparationType).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EffectiveFrom, x.EffectiveTo });
        b.HasMany(x => x.Versions).WithOne(x => x.Template).HasForeignKey(x => new { x.TenantId, x.TemplateId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationExitInterviewTemplateVersionConfiguration : IEntityTypeConfiguration<SeparationExitInterviewTemplateVersion>
{
    public void Configure(EntityTypeBuilder<SeparationExitInterviewTemplateVersion> b)
    {
        b.ToTable("SeparationExitInterviewTemplateVersions"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1);
        b.HasIndex(x => new { x.TenantId, x.TemplateId, x.VersionNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.Status, x.EffectiveFrom });
        b.HasMany(x => x.Questions).WithOne(x => x.TemplateVersion).HasForeignKey(x => new { x.TenantId, x.TemplateVersionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationExitInterviewQuestionConfiguration : IEntityTypeConfiguration<SeparationExitInterviewQuestion>
{
    public void Configure(EntityTypeBuilder<SeparationExitInterviewQuestion> b)
    {
        b.ToTable("SeparationExitInterviewQuestions"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b); b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.QuestionText).HasMaxLength(4000).IsRequired(); b.Property(x => x.QuestionType).HasConversion<int>();
        b.HasIndex(x => new { x.TenantId, x.TemplateVersionId, x.Code }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.TemplateVersionId, x.Sequence }); b.HasMany(x => x.Options).WithOne(x => x.Question).HasForeignKey(x => new { x.TenantId, x.QuestionId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationExitInterviewQuestionOptionConfiguration : IEntityTypeConfiguration<SeparationExitInterviewQuestionOption>
{
    public void Configure(EntityTypeBuilder<SeparationExitInterviewQuestionOption> b)
    {
        b.ToTable("SeparationExitInterviewQuestionOptions"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b); b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Label).HasMaxLength(300).IsRequired(); b.HasIndex(x => new { x.TenantId, x.QuestionId, x.Code }).IsUnique();
    }
}

public sealed class SeparationExitInterviewConfiguration : IEntityTypeConfiguration<SeparationExitInterview>
{
    public void Configure(EntityTypeBuilder<SeparationExitInterview> b)
    {
        b.ToTable("SeparationExitInterviews"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.RehireRecommendation).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1); b.Property(x => x.PrimaryReasonCategory).HasMaxLength(160); b.Property(x => x.SecondaryReasonCategory).HasMaxLength(160); b.Property(x => x.ReasonComment).HasMaxLength(4000); b.Property(x => x.RehireRecommendationReason).HasMaxLength(4000); b.Property(x => x.CompletionReason).HasMaxLength(2000);
        b.HasIndex(x => new { x.TenantId, x.EmployeeSeparationId }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.Status }); b.HasIndex(x => new { x.TenantId, x.AssignedHrUserId, x.Status }); b.HasOne(x => x.EmployeeSeparation).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeSeparationId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Responses).WithOne(x => x.ExitInterview).HasForeignKey(x => new { x.TenantId, x.ExitInterviewId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasMany(x => x.HrNotes).WithOne(x => x.ExitInterview).HasForeignKey(x => new { x.TenantId, x.ExitInterviewId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasMany(x => x.Events).WithOne(x => x.ExitInterview).HasForeignKey(x => new { x.TenantId, x.ExitInterviewId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasMany(x => x.ResponseRevisions).WithOne().HasForeignKey(x => new { x.TenantId, x.ExitInterviewId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SeparationExitInterviewResponseConfiguration : IEntityTypeConfiguration<SeparationExitInterviewResponse>
{
    public void Configure(EntityTypeBuilder<SeparationExitInterviewResponse> b) { b.ToTable("SeparationExitInterviewResponses"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b); b.Property(x => x.ResponseText).HasMaxLength(8000); b.Property(x => x.SelectedOptionCodes).HasMaxLength(4000); b.Property(x => x.Comment).HasMaxLength(4000); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1); b.HasIndex(x => new { x.TenantId, x.ExitInterviewId, x.QuestionId }).IsUnique(); }
}
public sealed class SeparationExitInterviewResponseRevisionConfiguration : IEntityTypeConfiguration<SeparationExitInterviewResponseRevision>
{
    public void Configure(EntityTypeBuilder<SeparationExitInterviewResponseRevision> b) { b.ToTable("SeparationExitInterviewResponseRevisions"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b); b.Property(x => x.ResponseText).HasMaxLength(8000); b.Property(x => x.SelectedOptionCodes).HasMaxLength(4000); b.Property(x => x.Comment).HasMaxLength(4000); b.HasIndex(x => new { x.TenantId, x.ExitInterviewId, x.QuestionId, x.RecordedAtUtc }); }
}
public sealed class SeparationExitInterviewHrNoteConfiguration : IEntityTypeConfiguration<SeparationExitInterviewHrNote>
{
    public void Configure(EntityTypeBuilder<SeparationExitInterviewHrNote> b) { b.ToTable("SeparationExitInterviewHrNotes"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b); b.Property(x => x.NoteText).HasMaxLength(8000).IsRequired(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken().HasDefaultValue(1); b.HasIndex(x => new { x.TenantId, x.ExitInterviewId, x.CreatedDate }); }
}
public sealed class SeparationExitInterviewEventConfiguration : IEntityTypeConfiguration<SeparationExitInterviewEvent>
{
    public void Configure(EntityTypeBuilder<SeparationExitInterviewEvent> b) { b.ToTable("SeparationExitInterviewEvents"); b.HasKey(x => x.Id); SeparationClearanceConfigurationHelpers.Tenant(b); b.Property(x => x.EventType).HasConversion<int>(); b.Property(x => x.FromStatus).HasConversion<int>(); b.Property(x => x.ToStatus).HasConversion<int>(); b.Property(x => x.Reason).HasMaxLength(2000); b.Property(x => x.MetadataJson).HasMaxLength(8000); b.HasIndex(x => new { x.TenantId, x.ExitInterviewId, x.OccurredAtUtc }); }
}
