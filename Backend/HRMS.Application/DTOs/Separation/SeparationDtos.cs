using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Separation;

public sealed record SeparationReasonDto(Guid Id, string Code, string Name, string? Description, SeparationReasonCategory Category, bool EmployeeInitiatedAllowed, bool EmployerInitiatedAllowed, bool IsActive, DateOnly EffectiveFrom, DateOnly? EffectiveTo, int DisplayOrder);
public sealed record SeparationReasonRequest(string Code, string Name, string? Description, SeparationReasonCategory Category, bool EmployeeInitiatedAllowed = true, bool EmployerInitiatedAllowed = true, bool IsActive = true, DateOnly? EffectiveFrom = null, DateOnly? EffectiveTo = null, int DisplayOrder = 0);
public sealed record SeparationRequest(Guid ReasonId, DateOnly RequestDate, DateOnly ProposedLastWorkingDate, string? Remarks);
public sealed record HrSeparationRequest(Guid ReasonId, DateOnly RequestDate, DateOnly ProposedLastWorkingDate, string? Remarks);
public sealed record EmployeeSeparationDto(Guid Id, Guid EmployeeId, string SeparationNumber, SeparationType SeparationType, Guid ReasonId, string ReasonCode, string ReasonName, EmployeeSeparationInitiator InitiatedBy, DateOnly RequestDate, DateOnly ProposedLastWorkingDate, DateOnly? ApprovedLastWorkingDate, DateOnly? NoticeStartDate, DateOnly? NoticeEndDate, DateOnly? ExpectedNoticeEndDate, int? NoticePeriodDays, int? NoticeServedDays, int? NoticeShortfallDays, int WaivedNoticeDays, int NoticeExtensionDays, NoticeDisposition NoticeDisposition, string? EmployeeRemarks, string? ManagerRemarks, string? HrRemarks, EmployeeSeparationStatus Status, int ConcurrencyVersion, DateTime CreatedDate);
public sealed record SeparationNoticeDto(Guid SeparationId, int? RequiredNoticeDays, DateOnly? NoticeStartDate, DateOnly? ExpectedNoticeEndDate, DateOnly? ApprovedLastWorkingDate, int? ServedNoticeDays, int WaivedNoticeDays, int? ShortfallDays, int ExtensionDays, NoticePeriodStatus NoticeStatus, string? LastRevisionReason, int ConcurrencyVersion);
public sealed record SeparationEventDto(Guid Id, EmployeeSeparationEventType EventType, EmployeeSeparationStatus? FromStatus, EmployeeSeparationStatus? ToStatus, Guid? ActorUserId, DateTime OccurredAtUtc, string? Reason, string? Comment);
public sealed record SeparationLwdRevisionRequest(DateOnly NewLastWorkingDate, string Reason);
public sealed record NoticeWaiverRequest(int WaiverDays, string Reason);
