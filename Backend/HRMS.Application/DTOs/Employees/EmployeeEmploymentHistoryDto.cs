using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

/// <summary>
/// Read DTO for an employee position history record. All organizational fields return both
/// the FK ID and the resolved Code+Name from the master table.
/// </summary>
public record EmployeeEmploymentHistoryDto(
    Guid Id,
    Guid EmployeeId,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,

    // Organizational FK references + display names
    Guid? HoldingCompanyId, string? HoldingCompanyCode, string? HoldingCompanyName,
    Guid? LobId, string? LobCode, string? LobName,
    Guid? OrganisationId, string? OrganisationCode, string? OrganisationName,
    Guid? DepartmentId, string? DepartmentCode, string? DepartmentName,
    Guid? SubDepartmentId, string? SubDepartmentCode, string? SubDepartmentName,
    Guid? SectionId, string? SectionCode, string? SectionName,
    Guid? SubSectionId, string? SubSectionCode, string? SubSectionName,
    Guid? FunctionId, string? FunctionCode, string? FunctionName,
    Guid? SubFunctionId, string? SubFunctionCode, string? SubFunctionName,

    // Job classification
    Guid? GradeId, string? GradeCode, string? GradeName,
    Guid? DesignationId, string? DesignationCode, string? DesignationName,
    Guid? EmployeeTypeId, string? EmployeeTypeCode, string? EmployeeTypeName,

    // Location
    Guid? CountryLocationId, string? CountryLocationCode, string? CountryLocationName,
    Guid? WorkLocationId, string? WorkLocationCode, string? WorkLocationName,

    // Cost center
    Guid? CostCenterId, string? CostCenterCode, string? CostCenterName,

    // Reporting
    Guid? ManagerId, string? ManagerEmployeeCode, string? ManagerFullName,

    // Change metadata
    Guid? PositionChangeReasonId, string? PositionChangeReasonCode, string? PositionChangeReasonName,
    EmploymentChangeReason ChangeReason,
    string? ChangeReasonDescription,

    // Snapshot fields
    string? BusinessRole,
    string? GradeLevel,
    string? CareerGroup,
    EmploymentType EmploymentType,
    EmployeeStatus EmploymentStatus,

    // Audit
    string? CreatedBy,
    DateTime CreatedDate,
    DateTime? ModifiedDate);
