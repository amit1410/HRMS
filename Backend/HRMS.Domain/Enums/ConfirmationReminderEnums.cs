namespace HRMS.Domain.Enums;

/// <summary>
/// Who a confirmation-review reminder is addressed to. Each recipient is a separately claimed, sent and
/// retried notification occurrence, so one approaching-probation case produces up to one row per value.
/// </summary>
public enum ConfirmationReminderRecipientType
{
    Employee = 0,
    Manager = 1,
    Hrbp = 2
}
