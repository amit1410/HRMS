namespace HRMS.Application.DTOs.Employees;

public sealed record EmployeeCodeConfigurationAuditDto(
    Guid Id,
    Guid ConfigurationVersionId,
    string Action,
    Guid ActorUserId,
    DateTime OccurredAtUtc,
    string? OldValue,
    string NewValue);
