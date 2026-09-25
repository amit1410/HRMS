namespace HRMS.Domain.Enums;

public enum OvertimeCategory
{
    NormalDay = 1,
    WeekOff = 2,
    Holiday = 3
}

public enum OvertimeRequestStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Rejected = 4,
    Cancelled = 5,
    Finalized = 6
}

public enum OvertimeRoundingMode
{
    None = 1,
    Floor = 2,
    Ceiling = 3,
    Nearest = 4
}
