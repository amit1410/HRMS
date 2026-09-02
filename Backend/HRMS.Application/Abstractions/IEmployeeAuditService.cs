using HRMS.Application.Common;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface IEmployeeAuditService
{
    Task<Result<PagedResult<EmployeeAuditLogDto>>> GetAsync(Guid employeeId, AuditQuery query, CancellationToken cancellationToken = default);
    Task LogChangeAsync(
        Guid employeeId,
        string employeeCode,
        string module,
        string? section,
        string? entityName,
        Guid? recordId,
        string? fieldName,
        string? oldValue,
        string? newValue,
        AuditChangeType changeType,
        string changedBy,
        DateOnly? effectiveDate = null,
        string? reason = null,
        string? source = null,
        Guid? importBatchId = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default);
}
