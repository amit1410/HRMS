using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveBalanceSummaryReader
{
    Task<Result<IReadOnlyList<LeaveBalanceSummaryDto>>> GetMineAsync(CancellationToken cancellationToken = default);
}

public sealed record LeaveBalanceSummaryDto(
    string LeaveTypeCode,
    string LeaveTypeName,
    EntitlementMode EntitlementMode,
    string? LeavePeriodName,
    decimal? GrantedQuantity,
    decimal? ReservedQuantity,
    decimal? ConsumedQuantity,
    decimal? AvailableQuantity);
