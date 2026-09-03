using HRMS.Application.Common;

namespace HRMS.Application.Abstractions;

public enum EmployeeManagerResolutionStatus
{
    Resolved,
    NoApplicableEmployment,
    NoAssignedManager,
    ManagerNotEligible,
    InvalidManagerReference,
    OverlappingEmployment,
    ReportingCycle,
    LegacyConflict
}

public sealed record EmployeeManagerResolution(
    EmployeeManagerResolutionStatus Status,
    Guid EmployeeId,
    Guid? ManagerId,
    string? ManagerEmployeeCode,
    string? ManagerFullName,
    string Message);

public interface IEmployeeManagerResolver
{
    Task<Result<EmployeeManagerResolution>> ResolveAsync(
        Guid employeeId,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default);

    Task<bool> WouldCreateCycleAsync(
        Guid employeeId,
        Guid proposedManagerId,
        DateOnly asOfDate,
        CancellationToken cancellationToken = default);
}
