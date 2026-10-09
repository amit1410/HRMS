using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Confirmation;

/// <summary>Current per-tenant scheduler configuration. <see cref="IsConfigured"/> is false when the tenant
/// has no settings row yet (the values are then the global defaults).</summary>
public sealed record ConfirmationSchedulerSettingDto(
    bool IsConfigured,
    bool Enabled,
    int FrequencyMinutes,
    int DefaultTriggerDaysBeforeEnd,
    int? MaxAttempts,
    int? ClaimLeaseMinutes,
    bool ResendProcessed,
    bool NotifyEmployee,
    bool NotifyManager,
    bool NotifyHrbp,
    DateTime? LastRunAtUtc,
    DateTime? ModifiedDate,
    string? ModifiedBy);

public sealed record ConfirmationSchedulerSettingRequest(
    bool Enabled = true,
    int FrequencyMinutes = 60,
    int DefaultTriggerDaysBeforeEnd = 30,
    int? MaxAttempts = null,
    int? ClaimLeaseMinutes = null,
    bool ResendProcessed = false,
    bool NotifyEmployee = true,
    bool NotifyManager = true,
    bool NotifyHrbp = true);

/// <summary>One confirmation-reminder execution-log row (one recipient of one probation cycle).</summary>
public sealed record ConfirmationReminderDeliveryDto(
    Guid Id,
    Guid EmployeeId,
    Guid ConfirmationPolicyId,
    Guid ConfirmationPolicyVersionId,
    DateOnly ProbationEndDate,
    DateOnly TriggerDate,
    ConfirmationReminderRecipientType RecipientType,
    Guid? RecipientEmployeeId,
    string? RecipientEmail,
    string Status,
    int AttemptCount,
    DateTime? SentAtUtc,
    DateTime? ProcessedAtUtc,
    DateTime? NextAttemptAtUtc,
    string? LastError,
    DateTime CreatedDate);

public sealed class ConfirmationReminderLogQuery : PagedQuery
{
    public Guid? EmployeeId { get; set; }
    public string? Status { get; set; }
}
