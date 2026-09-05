using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveBalanceTransactionPoster
{
    Task<Result<LeaveBalanceCreditResult>> PostCreditAsync(
        LeaveBalanceCreditCommand command,
        CancellationToken cancellationToken = default);
}

public interface ILeaveBalanceReader
{
    Task<Result<LeaveBalanceSnapshot>> GetAsync(
        Guid tenantId,
        Guid employeeId,
        Guid leaveTypeId,
        Guid leavePeriodId,
        CancellationToken cancellationToken = default);
}

public interface ILeaveBalanceAccountingService
{
    Task<Result<LeaveBalanceAccountingResult>> ReserveAsync(LeaveBalanceAccountingCommand command, CancellationToken cancellationToken = default);
    Task<Result<LeaveBalanceAccountingResult>> ConsumeReservationAsync(LeaveBalanceAccountingCommand command, CancellationToken cancellationToken = default);
    Task<Result<LeaveBalanceAccountingResult>> ReleaseReservationAsync(LeaveBalanceAccountingCommand command, CancellationToken cancellationToken = default);
    Task<Result<LeaveBalanceAccountingResult>> RestoreConsumptionAsync(LeaveBalanceAccountingCommand command, CancellationToken cancellationToken = default);
}

public sealed record LeaveBalanceAccountingCommand(
    Guid TenantId,
    Guid LeaveRequestId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid LeavePeriodId,
    Guid LeavePolicyVersionId,
    Guid LeavePolicyRuleId,
    decimal Quantity,
    DateOnly EffectiveDate,
    LeaveBalanceActorType ActorType,
    Guid? ActorUserId,
    Guid? ActorEmployeeId,
    string? CorrelationId);

public sealed record LeaveBalanceAccountingResult(
    Guid TransactionId,
    Guid BalanceId,
    LeaveBalanceTransactionType TransactionType,
    decimal GrantedQuantity,
    decimal ReservedQuantity,
    decimal ConsumedQuantity,
    decimal AvailableQuantity,
    bool IsReplay);

public static class LeaveBalanceAccountingErrorCodes
{
    public const string BalanceNotInitialized = "BalanceNotInitialized";
    public const string InsufficientLeaveBalance = "InsufficientLeaveBalance";
    public const string UnsupportedOperation = "UnsupportedBalanceOperation";
}

public sealed record LeaveBalanceCreditCommand(
    Guid TenantId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid LeavePeriodId,
    LeaveBalanceTransactionType TransactionType,
    decimal Quantity,
    DateOnly EffectiveDate,
    Guid? LeavePolicyVersionId,
    Guid? LeavePolicyRuleId,
    LeaveBalanceSourceType SourceType,
    string? SourceReference,
    LeaveBalanceActorType ActorType,
    Guid? ActorUserId,
    Guid? ActorEmployeeId,
    string IdempotencyKey,
    string? CorrelationId);

public sealed record LeaveBalanceCreditResult(
    Guid TransactionId,
    Guid BalanceId,
    decimal GrantedQuantity,
    decimal ReservedQuantity,
    decimal ConsumedQuantity,
    decimal AvailableQuantity);

public sealed record LeaveBalanceSnapshot(
    Guid BalanceId,
    Guid TenantId,
    Guid EmployeeId,
    Guid LeaveTypeId,
    Guid LeavePeriodId,
    decimal GrantedQuantity,
    decimal ReservedQuantity,
    decimal ConsumedQuantity,
    decimal AvailableQuantity,
    byte[] RowVersion);
