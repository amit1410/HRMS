using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities.Separation;

public sealed class SeparationExitInterviewTemplate : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public SeparationType? AppliesToSeparationType { get; set; }
    public Guid? AppliesToReasonId { get; set; }
    public bool IsEmployeeSurveyEnabled { get; set; } = true;
    public bool IsHrInterviewEnabled { get; set; } = true;
    public Guid? CreatedByUserId { get; set; }
    public Guid? ModifiedByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public ICollection<SeparationExitInterviewTemplateVersion> Versions { get; set; } = new List<SeparationExitInterviewTemplateVersion>();
}

public sealed class SeparationExitInterviewTemplateVersion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }
    public int VersionNumber { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public SeparationExitInterviewTemplateVersionStatus Status { get; set; } = SeparationExitInterviewTemplateVersionStatus.Draft;
    public Guid? CreatedByUserId { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public SeparationExitInterviewTemplate? Template { get; set; }
    public ICollection<SeparationExitInterviewQuestion> Questions { get; set; } = new List<SeparationExitInterviewQuestion>();
}

public sealed class SeparationExitInterviewQuestion : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid TemplateVersionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public SeparationExitInterviewQuestionType QuestionType { get; set; }
    public int Sequence { get; set; }
    public bool IsRequired { get; set; }
    public bool IsEmployeeVisible { get; set; } = true;
    public bool IsHrOnly { get; set; }
    public bool AllowsComment { get; set; }
    public decimal? MinValue { get; set; }
    public decimal? MaxValue { get; set; }
    public bool IsActive { get; set; } = true;
    public SeparationExitInterviewTemplateVersion? TemplateVersion { get; set; }
    public ICollection<SeparationExitInterviewQuestionOption> Options { get; set; } = new List<SeparationExitInterviewQuestionOption>();
}

public sealed class SeparationExitInterviewQuestionOption : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid QuestionId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public bool IsActive { get; set; } = true;
    public SeparationExitInterviewQuestion? Question { get; set; }
}

public sealed class SeparationExitInterview : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeSeparationId { get; set; }
    public EmployeeSeparation? EmployeeSeparation { get; set; }
    public Guid EmployeeId { get; set; }
    public Guid TemplateVersionId { get; set; }
    public SeparationExitInterviewStatus Status { get; set; } = SeparationExitInterviewStatus.NotStarted;
    public DateTime AssignedAtUtc { get; set; }
    public DateTime? EmployeeSubmittedAtUtc { get; set; }
    public DateTime? HrStartedAtUtc { get; set; }
    public DateTime? HrCompletedAtUtc { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public DateTime? ReopenedAtUtc { get; set; }
    public Guid? AssignedHrUserId { get; set; }
    public string? PrimaryReasonCategory { get; set; }
    public string? SecondaryReasonCategory { get; set; }
    public string? ReasonComment { get; set; }
    public SeparationExitInterviewRehireRecommendation RehireRecommendation { get; set; } = SeparationExitInterviewRehireRecommendation.NotAssessed;
    public string? RehireRecommendationReason { get; set; }
    public bool CompletedWithoutEmployeeResponse { get; set; }
    public string? CompletionReason { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public ICollection<SeparationExitInterviewResponse> Responses { get; set; } = new List<SeparationExitInterviewResponse>();
    public ICollection<SeparationExitInterviewResponseRevision> ResponseRevisions { get; set; } = new List<SeparationExitInterviewResponseRevision>();
    public ICollection<SeparationExitInterviewHrNote> HrNotes { get; set; } = new List<SeparationExitInterviewHrNote>();
    public ICollection<SeparationExitInterviewEvent> Events { get; set; } = new List<SeparationExitInterviewEvent>();
}

public sealed class SeparationExitInterviewResponse : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ExitInterviewId { get; set; }
    public Guid QuestionId { get; set; }
    public string? ResponseText { get; set; }
    public decimal? NumericValue { get; set; }
    public bool? BooleanValue { get; set; }
    public string? SelectedOptionCodes { get; set; }
    public string? Comment { get; set; }
    public bool SubmittedByEmployee { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public SeparationExitInterview? ExitInterview { get; set; }
}

public sealed class SeparationExitInterviewResponseRevision : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ExitInterviewId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid? ResponseId { get; set; }
    public string? ResponseText { get; set; }
    public decimal? NumericValue { get; set; }
    public bool? BooleanValue { get; set; }
    public string? SelectedOptionCodes { get; set; }
    public string? Comment { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTime RecordedAtUtc { get; set; }
}

public sealed class SeparationExitInterviewHrNote : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ExitInterviewId { get; set; }
    public string NoteText { get; set; } = string.Empty;
    public bool IsConfidential { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    public int ConcurrencyVersion { get; set; } = 1;
    public SeparationExitInterview? ExitInterview { get; set; }
}

public sealed class SeparationExitInterviewEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ExitInterviewId { get; set; }
    public SeparationExitInterviewEventType EventType { get; set; }
    public SeparationExitInterviewStatus? FromStatus { get; set; }
    public SeparationExitInterviewStatus? ToStatus { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public string? MetadataJson { get; set; }
    public SeparationExitInterview? ExitInterview { get; set; }
}
