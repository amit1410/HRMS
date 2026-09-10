namespace HRMS.Application.Abstractions;

public interface ILeaveApprovalReminderProcessor
{
    Task<LeaveReminderProcessingResult> ProcessAsync(CancellationToken cancellationToken = default);
}

public sealed record LeaveReminderProcessingResult(int Candidates, int Sent, int Skipped, int Failed);
