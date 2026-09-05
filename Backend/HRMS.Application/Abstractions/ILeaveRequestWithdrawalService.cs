using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveRequestWithdrawalService
{
    Task<Result<LeaveRequestWithdrawalResult>> WithdrawAsync(Guid requestId, CancellationToken cancellationToken = default);
}

public sealed record LeaveRequestWithdrawalResult(
    Guid RequestId,
    LeaveRequestStatus Status,
    LeaveRequestEventType EventType,
    DateTime OccurredAtUtc);

public static class LeaveRequestWithdrawalErrorCodes
{
    public const string InvalidStatusTransition = "InvalidStatusTransition";
    public const string AllocatedReservationNotFound = "AllocatedReservationNotFound";
}
