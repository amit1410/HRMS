using HRMS.Application.Common;

namespace HRMS.Application.DTOs.AccountEmployeeLinks;

public sealed record AccountEmployeeIdentityDto(string Status, Guid? Revision, Guid? LinkId, EmployeeIdentitySummaryDto? Employee, string EmploymentEligibility, DateOnly? BusinessDate);
public sealed record EmployeeIdentitySummaryDto(Guid Id, string DisplayName, string? EmployeeCode);
public sealed record AccountEmployeeCurrentStateDto(Guid UserId, string Status, AccountEmployeeCurrentDto? CurrentLink, Guid? Revision);
public sealed record AccountEmployeeCurrentDto(Guid LinkId, Guid EmployeeId, string DisplayName, string? EmployeeCode, Guid OriginalActorUserId, DateTime OriginalOccurredAtUtc);
public sealed record AccountEmployeeCandidateDto(Guid Id, string DisplayName, string? Email, string? EmployeeCode, string? Eligibility);
public sealed record AccountEmployeeLinkEventDto(Guid Id, long Sequence, string Operation, Guid ActorUserId, Guid? BeforeEmployeeId, Guid? AfterEmployeeId, string Reason, DateTime OccurredAtUtc);
public sealed record AccountEmployeeLinkRequest(Guid EmployeeId, Guid? ExpectedRevision, string Reason);
public sealed record AccountEmployeeUnlinkRequest(Guid ExpectedLinkId, Guid ExpectedEmployeeId, Guid? ExpectedRevision, string Reason);
public sealed record AccountEmployeeReplaceRequest(Guid ExpectedLinkId, Guid ExpectedEmployeeId, Guid? ExpectedRevision, Guid NewEmployeeId, string Reason);
public sealed record AccountEmployeeQuery(int Page = 1, int PageSize = 25, string? Search = null);
public sealed record AccountEmployeeHistoryQuery(int Page = 1, int PageSize = 25);
