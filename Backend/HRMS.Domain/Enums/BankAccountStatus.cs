namespace HRMS.Domain.Enums;

/// <summary>
/// Lifecycle status of an employee's bank account record. Only Active records can be current;
/// Frozen and Closed records are retained as history and replaced with a new row when needed.
/// </summary>
public enum BankAccountStatus
{
    Active = 0,
    Frozen = 1,
    Closed = 2
}
