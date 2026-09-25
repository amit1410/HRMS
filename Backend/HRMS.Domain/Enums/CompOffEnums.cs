namespace HRMS.Domain.Enums;

public enum CompOffSourceType
{
    WeekOff = 1,
    Holiday = 2,
    Overtime = 3
}

public enum CompOffBenefitMode
{
    CompOffOnly = 1,
    OvertimeOnly = 2,
    EmployeeChoice = 3,
    PolicyChoice = 4,
    DualBenefit = 5
}

public enum CompOffRoundingMode
{
    None = 1,
    Floor = 2,
    Ceiling = 3,
    Nearest = 4
}

public enum CompOffEarningStatus
{
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3,
    Superseded = 4,
    Expired = 5
}

public enum CompOffLedgerEntryType
{
    Credit = 1,
    Reserve = 2,
    Release = 3,
    Consume = 4,
    Restore = 5,
    Expire = 6,
    Adjustment = 7,
    Reversal = 8
}
