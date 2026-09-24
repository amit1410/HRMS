using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Separation;

public sealed record SeparationExitExecuteRequest(int ExpectedConcurrencyVersion = 0, string? IdempotencyKey = null, string? Comment = null);
public sealed record SeparationExitRetryRequest(string? Reason = null, string? IdempotencyKey = null);
public sealed record SeparationExitReadinessDto(Guid SeparationId, bool IsReady, IReadOnlyList<SeparationExitBlockerDto> Blockers, DateOnly? FinalLastWorkingDate, DateOnly BusinessDate, SeparationExitExecutionStatus Status);
public sealed record SeparationExitBlockerDto(string Code, string Message);
public sealed record SeparationExitExecutionDto(Guid Id, Guid SeparationId, Guid EmployeeId, SeparationExitExecutionStatus Status, DateOnly? FinalLastWorkingDate, DateTime? StartedAtUtc, DateTime? EmploymentExecutedAtUtc, DateTime? AccessDeprovisionedAtUtc, DateTime? CompletedAtUtc, string? FailureCode, string? FailureMessage, int ConcurrencyVersion);
public sealed record SeparationExitExecutionEventDto(SeparationExitExecutionEventType EventType, string? Step, DateTime OccurredAtUtc, Guid? ActorUserId, string? Reason);
public sealed class SeparationExitDashboardQuery { public int Page { get; set; } = 1; public int PageSize { get; set; } = 50; public string? Status { get; set; } public string? EmployeeSearch { get; set; } public DateOnly? LwdFrom { get; set; } public DateOnly? LwdTo { get; set; } }
public sealed record SeparationExitDashboardItemDto(Guid SeparationId, Guid EmployeeId, string? EmployeeCode, string EmployeeName, DateOnly? FinalLastWorkingDate, EmployeeSeparationStatus SeparationStatus, SeparationExitExecutionStatus ExecutionStatus, EmployeeStatus EmploymentStatus, bool Ready, int BlockerCount, DateTime? ClosedAtUtc);
