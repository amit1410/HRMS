using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// A centralized audit log entry for tracking all important changes to employee data.
/// Each record captures who changed what, when, and why — including bulk import traces
/// via <see cref="ImportBatchId"/>.
/// </summary>
public class EmployeeAuditLog : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    public string? EmployeeCode { get; set; }

    /// <summary>Top-level module, e.g. "Employee", "Employment", "Contact".</summary>
    public string Module { get; set; } = string.Empty;

    /// <summary>Sub-section within the module, e.g. "Personal", "Bank", "Education".</summary>
    public string? Section { get; set; }

    /// <summary>The entity or table name affected, e.g. "EmployeeFamily", "EmployeeBankDetail".</summary>
    public string? EntityName { get; set; }

    /// <summary>The primary key of the affected record.</summary>
    public Guid? RecordId { get; set; }

    /// <summary>The field that was changed.</summary>
    public string? FieldName { get; set; }

    public string? OldValue { get; set; }

    public string? NewValue { get; set; }

    public AuditChangeType ChangeType { get; set; }

    /// <summary>The effective date of the change (for employment changes).</summary>
    public DateOnly? EffectiveDate { get; set; }

    /// <summary>User who made the change (email or employee code).</summary>
    public string ChangedBy { get; set; } = string.Empty;

    /// <summary>Free-text reason for the change.</summary>
    public string? Reason { get; set; }

    /// <summary>Source of the change: "Manual", "Import", "API", etc.</summary>
    public string? Source { get; set; }

    /// <summary>Batch ID linking this entry to a bulk import operation.</summary>
    public Guid? ImportBatchId { get; set; }

    public string? IpAddress { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
}
