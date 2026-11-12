namespace HRMS.Domain.Enums;

public enum EmployeeImportOperationState
{
    Prepared = 0,
    Claimed = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4,
    PartiallyCompleted = 5,
    Cancelled = 6,
    ArtifactPending = 7,
    Queued = 8
}

public enum EmployeeImportRowStatus
{
    Pending = 0,
    Processing = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4
}
