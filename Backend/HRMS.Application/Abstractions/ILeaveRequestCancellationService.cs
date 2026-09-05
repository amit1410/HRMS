using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveRequestCancellationService
{
    Task<Result<LeaveRequestCancellationResult>> CancelAsync(Guid requestId, CancellationToken cancellationToken = default);
}

public sealed record LeaveRequestCancellationResult(
    Guid RequestId,
    LeaveRequestStatus Status,
    LeaveRequestEventType EventType,
    DateTime OccurredAtUtc);

public static class LeaveRequestCancellationErrorCodes
{
    public const string InvalidStatusTransition = "InvalidStatusTransition";
    public const string CancellationNotAllowed = "CancellationNotAllowed";
    public const string UnsupportedConfiguration = "UnsupportedConfiguration";
    public const string AllocatedCancellationBalanceReleaseNotReady = "AllocatedCancellationBalanceReleaseNotReady";
    public const string AllocatedConsumptionNotFound = "AllocatedConsumptionNotFound";
}
