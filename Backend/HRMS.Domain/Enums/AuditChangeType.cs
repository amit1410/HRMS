namespace HRMS.Domain.Enums;

/// <summary>
/// Type of change recorded in the employee audit log.
/// </summary>
public enum AuditChangeType
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Import = 4,
    StatusChange = 5,
    EmploymentChange = 6,
    DocumentUpload = 7,
    DocumentDelete = 8
}
