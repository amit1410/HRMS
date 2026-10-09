using HRMS.Domain.Enums;

namespace HRMS.Application.Services;

/// <summary>
/// Pure date arithmetic for probation/confirmation. Kept separate from the resolver and service so the
/// rules are unit-testable without a database. The Date of Joining is always the single source of truth
/// (<c>Employee.DateOfJoining</c>); no confirmation-related date is maintained by hand.
/// </summary>
public static class ConfirmationDateCalculator
{
    /// <summary>
    /// The date probation ends = Date of Joining + the configured probation duration. Months are added
    /// calendar-wise (clamped to the end of the target month); days are added directly.
    /// </summary>
    public static DateOnly ProbationEndDate(DateOnly dateOfJoining, int duration, ProbationDurationUnit unit) =>
        AddDuration(dateOfJoining, duration, unit);

    /// <summary>
    /// A new probation-end date after an extension, applied from the current probation end.
    /// </summary>
    public static DateOnly ExtendedProbationEndDate(DateOnly currentProbationEnd, int extensionDuration, ProbationDurationUnit unit) =>
        AddDuration(currentProbationEnd, extensionDuration, unit);

    /// <summary>
    /// The date the scheduler should first act on an employee = probation end minus the configured
    /// trigger window. Never earlier than the Date of Joining.
    /// </summary>
    public static DateOnly ReviewTriggerDate(DateOnly dateOfJoining, DateOnly probationEndDate, int triggerDaysBeforeEnd)
    {
        var trigger = probationEndDate.AddDays(-Math.Max(0, triggerDaysBeforeEnd));
        return trigger < dateOfJoining ? dateOfJoining : trigger;
    }

    private static DateOnly AddDuration(DateOnly from, int duration, ProbationDurationUnit unit) => unit switch
    {
        ProbationDurationUnit.Days => from.AddDays(duration),
        _ => from.AddMonths(duration)
    };
}
