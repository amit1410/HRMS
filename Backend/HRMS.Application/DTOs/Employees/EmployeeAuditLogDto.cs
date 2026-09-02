using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public record EmployeeAuditLogDto(
    Guid Id,
    Guid EmployeeId,
    string? EmployeeCode,
    string Module,
    string? Section,
    string? EntityName,
    Guid? RecordId,
    string? FieldName,
    string? OldValue,
    string? NewValue,
    AuditChangeType ChangeType,
    DateOnly? EffectiveDate,
    string ChangedBy,
    string? Reason,
    string? Source,
    Guid? ImportBatchId,
    string? IpAddress,
    DateTime CreatedDate);
