namespace HRMS.Application.Abstractions;

public interface IEffectiveEmploymentResolver
{
    Task<EffectiveEmploymentResolutionResult> ResolveAsync(
        Guid tenantId,
        Guid employeeId,
        DateOnly effectiveDate,
        CancellationToken cancellationToken = default);
}

public enum EffectiveEmploymentResolutionStatus
{
    Resolved,
    NotFound,
    ConfigurationAmbiguity,
    InvalidTenant
}

public sealed record EffectiveEmploymentResolutionResult(
    EffectiveEmploymentResolutionStatus Status,
    Guid TenantId,
    Guid EmployeeId,
    DateOnly EffectiveDate,
    EffectiveEmploymentSnapshot? Employment,
    string Message);

/// <summary>
/// Date-specific IDs used by Leave applicability. Display names and mutable Employee fields are
/// deliberately excluded so policy evaluation remains historical and deterministic.
/// </summary>
public sealed record EffectiveEmploymentSnapshot(
    Guid HistoryId,
    Guid TenantId,
    Guid EmployeeId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    Guid? HoldingCompanyId,
    Guid? LobId,
    Guid? OrganisationId,
    Guid? DepartmentId,
    Guid? SubDepartmentId,
    Guid? SectionId,
    Guid? SubSectionId,
    Guid? FunctionId,
    Guid? SubFunctionId,
    Guid? GradeId,
    Guid? DesignationId,
    Guid? EmployeeTypeId,
    Guid? CountryLocationId,
    Guid? WorkLocationId,
    Guid? CostCenterId,
    Guid? ManagerId,
    HRMS.Domain.Enums.EmploymentType EmploymentType,
    HRMS.Domain.Enums.EmployeeStatus EmploymentStatus,
    DateOnly DateOfJoining,
    DateOnly? GroupDateOfJoining,
    DateOnly? DateOfLeaving,
    HRMS.Domain.Enums.Gender Gender);
