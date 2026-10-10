using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Employees;

/// <summary>The only workflow sources allowed to change an employee Job Status.</summary>
public enum JobStatusMutationSource
{
    Creation = 1,
    Confirmation = 2,
    Separation = 3,
    Employment = 4
}

public sealed record JobStatusChangeRequest(
    Guid EmployeeId,
    string? JobStatus,
    JobStatusMutationSource Source,
    string? Reason = null);

public sealed record JobStatusChangeResult(
    Guid EmployeeId,
    string? PreviousJobStatus,
    string JobStatus,
    JobStatusMutationSource Source);
