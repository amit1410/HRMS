namespace HRMS.Domain.Enums;

/// <summary>Authoritative lifecycle state of an employee's notice period.</summary>
public enum NoticePeriodStatus
{
    NotServing = 0,
    Active = 1,
    Completed = 2,
    Cancelled = 3
}
