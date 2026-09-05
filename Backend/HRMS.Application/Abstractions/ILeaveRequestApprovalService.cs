using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveRequestApprovalService
{
    Task<Result<LeaveRequestApprovalResult>> ApproveAsync(Guid requestId, CancellationToken cancellationToken = default);
    Task<Result<LeaveRequestApprovalResult>> RejectAsync(Guid requestId, CancellationToken cancellationToken = default);
}

public sealed record LeaveRequestApprovalResult(
    Guid RequestId,
    LeaveRequestStatus Status,
    LeaveRequestEventType EventType,
    DateTime OccurredAtUtc);

public static class LeaveRequestApprovalErrorCodes
{
    public const string ApproverNotAuthorized = "ApproverNotAuthorized";
    public const string InvalidStatusTransition = "InvalidStatusTransition";
    public const string ConfigurationAmbiguity = "ConfigurationAmbiguity";
    public const string AllocatedReservationNotFound = "AllocatedReservationNotFound";
}
