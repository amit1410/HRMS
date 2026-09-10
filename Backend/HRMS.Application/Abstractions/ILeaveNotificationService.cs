using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface ILeaveNotificationService
{
    Task NotifyAsync(Guid requestId, LeaveRequestEventType eventType, CancellationToken cancellationToken = default);
}
