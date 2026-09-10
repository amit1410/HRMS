using HRMS.Application.Common;

namespace HRMS.Application.Abstractions;

public interface ILeaveBalanceSummaryReader
{
    Task<Result<IReadOnlyList<LeaveBalanceSummaryDto>>> GetMineAsync(CancellationToken cancellationToken = default);
}

public sealed record LeaveBalanceSummaryDto(
    Guid BalanceId,
    Guid LeaveTypeId,
    string LeaveTypeCode,
    string LeaveTypeName,
    Guid LeavePeriodId,
    string LeavePeriodCode,
    string LeavePeriodName,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    decimal GrantedQuantity,
    decimal ReservedQuantity,
    decimal ConsumedQuantity,
    decimal AvailableQuantity);
