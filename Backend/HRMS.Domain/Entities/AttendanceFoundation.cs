using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class Shift : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string ShiftCode { get; set; } = string.Empty;
    public string ShiftName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public int BreakDurationMinutes { get; set; }
    public int MinimumWorkMinutes { get; set; }
    public int FullDayWorkMinutes { get; set; }
    public int? HalfDayWorkMinutes { get; set; }
    public int GraceInMinutes { get; set; }
    public int GraceOutMinutes { get; set; }
    public int LateThresholdMinutes { get; set; }
    public int EarlyOutThresholdMinutes { get; set; }
    public bool IsNightShift { get; set; }
    public bool CrossesMidnight { get; set; }
    public AttendanceCaptureMode CaptureMode { get; set; } = AttendanceCaptureMode.BiometricOnly;
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public string? CreatedBy { get; set; }
    public string? ModifiedBy { get; set; }
}

public sealed class ShiftPattern : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int CycleLengthDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public ICollection<ShiftPatternDay> Days { get; set; } = new List<ShiftPatternDay>();
}

public sealed class ShiftPatternDay : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid ShiftPatternId { get; set; }
    public int SequenceDay { get; set; }
    public Guid? ShiftId { get; set; }
    public ShiftPatternDayType DayType { get; set; }
    public ShiftPattern? ShiftPattern { get; set; }
    public Shift? Shift { get; set; }
}

public sealed class ShiftApplicabilityRule : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid? ShiftId { get; set; }
    public Guid? ShiftPatternId { get; set; }
    public int Priority { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public HRMS.Domain.Enums.Gender? Gender { get; set; }
    public Guid? HoldingCompanyId { get; set; }
    public Guid? LobId { get; set; }
    public Guid? OrganisationId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? SubDepartmentId { get; set; }
    public Guid? SectionId { get; set; }
    public Guid? SubSectionId { get; set; }
    public Guid? FunctionId { get; set; }
    public Guid? SubFunctionId { get; set; }
    public Guid? GradeId { get; set; }
    public Guid? DesignationId { get; set; }
    public Guid? EmployeeTypeId { get; set; }
    public Guid? CountryLocationId { get; set; }
    public Guid? WorkLocationId { get; set; }
    public Guid? CostCenterId { get; set; }
}

public sealed class EmployeeRosterDay : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly RosterDate { get; set; }
    public Guid? ShiftId { get; set; }
    public Guid? ShiftPatternId { get; set; }
    public RosterDayType DayType { get; set; }
    public RosterAssignmentSource AssignmentSource { get; set; }
    public bool IsOverride { get; set; }
    public bool IsCalendarOverride { get; set; }
    public Guid? OriginalShiftId { get; set; }
    public string? Comment { get; set; }
    public Guid? EmployeeEmploymentHistoryId { get; set; }
}

public sealed class EmployeeRosterChangeHistory : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public DateOnly RosterDate { get; set; }
    public Guid? PreviousShiftId { get; set; }
    public Guid? NewShiftId { get; set; }
    public RosterDayType PreviousDayType { get; set; }
    public RosterDayType NewDayType { get; set; }
    public RosterAssignmentSource Source { get; set; }
    public string? Reason { get; set; }
    public Guid? UploadBatchId { get; set; }
    public string? ChangedBy { get; set; }
}

public sealed class RosterUploadBatch : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public RosterUploadStatus Status { get; set; } = RosterUploadStatus.Validating;
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int InvalidRows { get; set; }
    public int CommittedRows { get; set; }
    public string? FailureReason { get; set; }
    public ICollection<RosterUploadRow> Rows { get; set; } = new List<RosterUploadRow>();
}

public sealed class RosterUploadRow : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid RosterUploadBatchId { get; set; }
    public int RowNumber { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public DateOnly RosterDate { get; set; }
    public string? ShiftCode { get; set; }
    public RosterDayType DayType { get; set; }
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public RosterUploadBatch? Batch { get; set; }
}
