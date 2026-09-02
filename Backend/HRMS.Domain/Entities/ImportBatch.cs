using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// Metadata for a bulk import batch. Tracks the upload lifecycle from validation through
/// processing to completion or failure. Individual import errors are linked via
/// <see cref="ImportBatchId"/> on <see cref="EmployeeAuditLog"/>.
/// </summary>
public class ImportBatch : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    /// <summary>User-initiated batch name or auto-generated description.</summary>
    public string? FileName { get; set; }

    /// <summary>User who initiated the import (email or employee code).</summary>
    public string ImportedBy { get; set; } = string.Empty;

    public int TotalRows { get; set; }

    public int SuccessfulRows { get; set; }

    public int FailedRows { get; set; }

    public int SkippedRows { get; set; }

    /// <summary>Current state of the import: "Validating", "Processing", "Completed", "Failed", "RolledBack".</summary>
    public string Status { get; set; } = "Validating";

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Summary message or error description.</summary>
    public string? Message { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
