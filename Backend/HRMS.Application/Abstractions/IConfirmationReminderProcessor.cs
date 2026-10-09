namespace HRMS.Application.Abstractions;

/// <summary>
/// Scans the current tenant for employees approaching probation completion and raises confirmation-review
/// reminders to the employee, their manager and HRBP — once per recipient per probation cycle, idempotently.
/// The caller (the worker) establishes the tenant scope before calling.
/// </summary>
public interface IConfirmationReminderProcessor
{
    Task<ConfirmationReminderProcessingResult> ProcessAsync(CancellationToken cancellationToken = default);
}

/// <param name="Cases">Approaching-probation employees examined this scan.</param>
/// <param name="Sent">Reminder emails delivered.</param>
/// <param name="Skipped">Occurrences skipped (already notified, no recipient, or claimed elsewhere).</param>
/// <param name="Failed">Occurrences whose delivery failed and will be retried.</param>
public sealed record ConfirmationReminderProcessingResult(int Cases, int Sent, int Skipped, int Failed);
