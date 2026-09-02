using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// Metadata for an uploaded employee document. The actual file is stored on disk or blob storage;
/// this entity records the reference, type, and upload audit trail.
/// </summary>
public class EmployeeDocument : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>Optional link to the specific previous-employment record this document supports.</summary>
    public Guid? PreviousEmploymentId { get; set; }

    public string DocumentName { get; set; } = string.Empty;

    public DocumentCategory DocumentCategory { get; set; } = DocumentCategory.Other;

    public string? DocumentNumber { get; set; }

    /// <summary>Server path or blob URL where the file is stored.</summary>
    public string FilePath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string ContentType { get; set; } = string.Empty;

    /// <summary>User who uploaded this document (employee code or user email).</summary>
    public string? UploadedBy { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
    public EmployeePreviousEmployment? PreviousEmployment { get; set; }
}
