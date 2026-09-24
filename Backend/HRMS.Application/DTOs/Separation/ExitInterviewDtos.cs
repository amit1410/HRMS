using HRMS.Domain.Enums;
using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Separation;

public sealed record ExitInterviewTemplateRequest(string Code, string Name, string? Description, DateOnly EffectiveFrom, DateOnly? EffectiveTo, SeparationType? AppliesToSeparationType, Guid? AppliesToReasonId, bool IsEmployeeSurveyEnabled = true, bool IsHrInterviewEnabled = true);
public sealed record ExitInterviewTemplateDto(Guid Id, string Code, string Name, string? Description, bool IsActive, DateOnly EffectiveFrom, DateOnly? EffectiveTo, IReadOnlyList<ExitInterviewTemplateVersionDto> Versions);
public sealed record ExitInterviewTemplateVersionDto(Guid Id, Guid TemplateId, int VersionNumber, DateOnly EffectiveFrom, DateOnly? EffectiveTo, SeparationExitInterviewTemplateVersionStatus Status, IReadOnlyList<ExitInterviewQuestionDto> Questions);
public sealed record ExitInterviewQuestionOptionRequest(string Code, string Label, int Sequence = 0);
public sealed record ExitInterviewQuestionRequest(string Code, string QuestionText, SeparationExitInterviewQuestionType QuestionType, int Sequence, bool IsRequired, bool IsEmployeeVisible = true, bool IsHrOnly = false, bool AllowsComment = false, decimal? MinValue = null, decimal? MaxValue = null, IReadOnlyList<ExitInterviewQuestionOptionRequest>? Options = null);
public sealed record ExitInterviewVersionRequest(int VersionNumber, DateOnly EffectiveFrom, DateOnly? EffectiveTo, IReadOnlyList<ExitInterviewQuestionRequest> Questions);
public sealed record ExitInterviewAssignRequest(Guid? TemplateVersionId = null, Guid? AssignedHrUserId = null);
public sealed class ExitInterviewInboxQuery : PagedQuery { public string? Status { get; set; } public string? EmployeeSearch { get; set; } public DateOnly? LwdFrom { get; set; } public DateOnly? LwdTo { get; set; } public Guid? AssignedHrUserId { get; set; } }
public sealed record ExitInterviewResponseRequest(Guid QuestionId, string? ResponseText, decimal? NumericValue, bool? BooleanValue, IReadOnlyList<string>? SelectedOptionCodes, string? Comment);
public sealed record ExitInterviewDraftRequest(IReadOnlyList<ExitInterviewResponseRequest> Responses);
public sealed record ExitInterviewCompletionRequest(string? PrimaryReasonCategory, string? SecondaryReasonCategory, string? ReasonComment, SeparationExitInterviewRehireRecommendation RehireRecommendation, string? RehireRecommendationReason, bool CompletedWithoutEmployeeResponse = false, string? CompletionReason = null);
public sealed record ExitInterviewNoteRequest(string NoteText);
public sealed record ExitInterviewReopenRequest(string Reason);
public sealed record ExitInterviewQuestionDto(Guid Id, string Code, string QuestionText, SeparationExitInterviewQuestionType QuestionType, int Sequence, bool IsRequired, bool AllowsComment, decimal? MinValue, decimal? MaxValue, IReadOnlyList<ExitInterviewQuestionOptionDto> Options);
public sealed record ExitInterviewQuestionOptionDto(string Code, string Label, int Sequence);
public sealed record ExitInterviewResponseDto(Guid QuestionId, string? ResponseText, decimal? NumericValue, bool? BooleanValue, IReadOnlyList<string> SelectedOptionCodes, string? Comment);
public sealed record ExitInterviewEmployeeDto(Guid Id, Guid EmployeeSeparationId, SeparationExitInterviewStatus Status, DateTime AssignedAtUtc, DateTime? EmployeeSubmittedAtUtc, IReadOnlyList<ExitInterviewQuestionDto> Questions, IReadOnlyList<ExitInterviewResponseDto> Responses);
public sealed record ExitInterviewHrDto(Guid Id, Guid EmployeeSeparationId, Guid EmployeeId, string? EmployeeCode, string EmployeeName, string? SeparationReason, DateOnly? ApprovedLastWorkingDate, Guid? AssignedHrUserId, SeparationExitInterviewStatus Status, DateTime AssignedAtUtc, DateTime? EmployeeSubmittedAtUtc, string? PrimaryReasonCategory, string? SecondaryReasonCategory, string? ReasonComment, SeparationExitInterviewRehireRecommendation RehireRecommendation, string? RehireRecommendationReason, bool CompletedWithoutEmployeeResponse, string? CompletionReason, IReadOnlyList<ExitInterviewQuestionDto> Questions, IReadOnlyList<ExitInterviewResponseDto> Responses, IReadOnlyList<string> ConfidentialNotes);
public sealed record ExitInterviewInboxItemDto(Guid Id, Guid EmployeeSeparationId, Guid EmployeeId, string? EmployeeCode, string EmployeeName, string? SeparationReason, DateOnly? ApprovedLastWorkingDate, SeparationExitInterviewStatus Status, DateTime AssignedAtUtc, DateTime? EmployeeSubmittedAtUtc, Guid? AssignedHrUserId, DateTime? HrCompletedAtUtc);
public sealed record ExitInterviewEventDto(SeparationExitInterviewEventType EventType, SeparationExitInterviewStatus? FromStatus, SeparationExitInterviewStatus? ToStatus, DateTime OccurredAtUtc, string? Reason);
public sealed record ExitInterviewAnalyticsDto(int AssignedCount, int EmployeeSubmittedCount, int CompletedCount, decimal? AverageRating, IReadOnlyDictionary<string, int> ReasonCategories, IReadOnlyDictionary<string, int> RehireRecommendations);
