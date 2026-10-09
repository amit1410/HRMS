using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Resolves the authoritative recipients of a confirmation-review reminder (the employee, their effective
/// reporting manager, and the tenant's HRBPs) and delivers the reminder email. Recipient resolution reuses
/// the existing employee/manager/account-link relationships; missing recipients are omitted (and the reason
/// is logged) rather than failing the case. No method here changes any confirmation outcome — Phase 9b only
/// notifies.
/// </summary>
public interface IConfirmationReminderNotificationService
{
    Task<IReadOnlyList<ConfirmationReminderRecipient>> ResolveRecipientsAsync(
        ConfirmationReminderCase reviewCase, ConfirmationRecipientToggles toggles, CancellationToken cancellationToken = default);

    Task<ConfirmationNotificationDeliveryResult> SendAsync(
        ConfirmationReminderCase reviewCase, ConfirmationReminderRecipient recipient, CancellationToken cancellationToken = default);
}

/// <summary>An approaching-probation employee whose confirmation review is due.</summary>
public sealed record ConfirmationReminderCase(
    Guid TenantId,
    Guid EmployeeId,
    string EmployeeName,
    Guid ConfirmationPolicyId,
    Guid ConfirmationPolicyVersionId,
    string PolicyName,
    DateOnly ProbationEndDate,
    DateOnly TriggerDate);

/// <summary>A resolved notification target for a case.</summary>
public sealed record ConfirmationReminderRecipient(
    ConfirmationReminderRecipientType Type,
    string Email,
    Guid? RecipientEmployeeId);

/// <summary>Which recipient kinds the tenant has enabled.</summary>
public sealed record ConfirmationRecipientToggles(bool Employee, bool Manager, bool Hrbp)
{
    public static readonly ConfirmationRecipientToggles All = new(true, true, true);
}

public enum ConfirmationNotificationDeliveryResult
{
    Sent,
    Skipped,
    Failed
}
