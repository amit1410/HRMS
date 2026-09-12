using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>Immutable privileged correction of effective attendance. Raw punches are never changed.</summary>
public sealed class AttendanceAdminCorrection : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly BusinessDate { get; set; }
    public DateTime? CorrectedInAtUtc { get; set; }
    public DateTime? CorrectedOutAtUtc { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int CorrectionVersion { get; set; }
}
