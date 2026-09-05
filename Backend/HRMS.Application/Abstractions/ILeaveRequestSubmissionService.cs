using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveRequestSubmissionService
{
    Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(
        LeaveRequestSubmissionInput input,
        CancellationToken cancellationToken = default);
}

/// <summary>Client input for the future authenticated submission operation.</summary>
public sealed record LeaveRequestSubmissionInput(
    Guid LeaveTypeId,
    DateOnly StartDate,
    DateOnly EndDate,
    string IdempotencyKey);

/// <summary>Authoritative values persisted by a successful submission.</summary>
public sealed record LeaveRequestSubmissionResult(
    Guid RequestId,
    LeaveRequestStatus Status,
    Guid EmployeeId,
    Guid EmployeeEmploymentHistoryId,
    Guid LeaveTypeId,
    Guid LeavePeriodId,
    Guid LeavePolicyVersionId,
    Guid LeavePolicyRuleId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    DateTime SubmittedAtUtc,
    IReadOnlyList<LeaveRequestSubmissionDay> RequestDays,
    bool IdempotentReplay);

public sealed record LeaveRequestSubmissionDay(
    DateOnly Date,
    decimal RequestedQuantity,
    decimal ChargeableQuantity,
    string? DayClassification,
    string? CalculationReason,
    bool IsEmployeeRequested);

/// <summary>
/// Acquires the per-employee persistence concurrency scope. The implementation is provider-specific;
/// future status-changing operations must use this same scope before changing overlap/counting status.
/// </summary>
public interface ILeaveRequestSubmissionLock
{
    Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default);
}

public static class LeaveRequestSubmissionErrorCodes
{
    public const string IdempotencyConflict = "IdempotencyConflict";
    public const string Overlap = "Overlap";
    public const string ConcurrencyConflict = "ConcurrencyConflict";
    public const string AllocatedBalanceReservationNotReady = "AllocatedBalanceReservationNotReady";
}
