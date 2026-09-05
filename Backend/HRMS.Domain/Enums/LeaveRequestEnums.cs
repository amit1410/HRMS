namespace HRMS.Domain.Enums;

public enum LeaveRequestStatus
{
    PendingApproval = 0,
    Approved = 1,
    Rejected = 2,
    Withdrawn = 3,
    Cancelled = 4
}

public enum LeaveRequestEventType
{
    Created = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3,
    Withdrawn = 4,
    Cancelled = 5
}
