using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveNotificationService
{
    Task NotifyAsync(Guid requestId, LeaveRequestEventType eventType, CancellationToken cancellationToken = default);
    Task<LeaveNotificationDeliveryResult> NotifyApprovalReminderAsync(Guid requestId, CancellationToken cancellationToken = default);
}

public enum LeaveNotificationDeliveryResult
{
    Sent,
    Skipped,
    Failed
}
