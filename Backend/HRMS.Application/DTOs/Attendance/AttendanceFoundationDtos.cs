using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Attendance;

public sealed class ShiftQuery : PagedQuery { public bool? IsActive { get; set; } }
public sealed class ShiftRequest
{
    public string ShiftCode { get; set; } = string.Empty; public string ShiftName { get; set; } = string.Empty; public string? Description { get; set; }
    public TimeOnly StartTime { get; set; } public TimeOnly EndTime { get; set; } public int BreakDurationMinutes { get; set; }
    public int MinimumWorkMinutes { get; set; } public int FullDayWorkMinutes { get; set; } public int? HalfDayWorkMinutes { get; set; }
    public int GraceInMinutes { get; set; } public int GraceOutMinutes { get; set; } public int LateThresholdMinutes { get; set; } public int EarlyOutThresholdMinutes { get; set; }
    public bool IsNightShift { get; set; } public bool CrossesMidnight { get; set; } public AttendanceCaptureMode CaptureMode { get; set; }
    public bool IsActive { get; set; } = true; public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public string? ConcurrencyToken { get; set; }
}
public sealed record ShiftDto(Guid Id, string ShiftCode, string ShiftName, string? Description, TimeOnly StartTime, TimeOnly EndTime, int BreakDurationMinutes, int MinimumWorkMinutes, int FullDayWorkMinutes, int? HalfDayWorkMinutes, int GraceInMinutes, int GraceOutMinutes, int LateThresholdMinutes, int EarlyOutThresholdMinutes, bool IsNightShift, bool CrossesMidnight, AttendanceCaptureMode CaptureMode, bool IsActive, DateOnly EffectiveFrom, DateOnly? EffectiveTo, DateTime CreatedDate, DateTime? ModifiedDate, string ConcurrencyToken);
public sealed class ShiftPatternRequest { public string Code { get; set; } = string.Empty; public string Name { get; set; } = string.Empty; public int CycleLengthDays { get; set; } public bool IsActive { get; set; } = true; public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public List<ShiftPatternDayRequest> Days { get; set; } = []; }
public sealed class ShiftPatternDayRequest { public int SequenceDay { get; set; } public Guid? ShiftId { get; set; } public ShiftPatternDayType DayType { get; set; } = ShiftPatternDayType.Shift; }
public sealed record ShiftPatternDto(Guid Id, string Code, string Name, int CycleLengthDays, bool IsActive, DateOnly EffectiveFrom, DateOnly? EffectiveTo, IReadOnlyList<ShiftPatternDayDto> Days);
public sealed record ShiftPatternDayDto(int SequenceDay, Guid? ShiftId, ShiftPatternDayType DayType);
public sealed class ShiftApplicabilityRequest
{
    public Guid? ShiftId { get; set; } public Guid? ShiftPatternId { get; set; } public int Priority { get; set; } public DateOnly EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; }
    public HRMS.Domain.Enums.Gender? Gender { get; set; } public Guid? HoldingCompanyId { get; set; } public Guid? LobId { get; set; } public Guid? OrganisationId { get; set; } public Guid? DepartmentId { get; set; } public Guid? SubDepartmentId { get; set; } public Guid? SectionId { get; set; } public Guid? SubSectionId { get; set; } public Guid? FunctionId { get; set; } public Guid? SubFunctionId { get; set; } public Guid? GradeId { get; set; } public Guid? DesignationId { get; set; } public Guid? EmployeeTypeId { get; set; } public Guid? CountryLocationId { get; set; } public Guid? WorkLocationId { get; set; } public Guid? CostCenterId { get; set; }
}
public sealed record ShiftResolutionDto(Guid EmployeeId, DateOnly Date, Guid? ShiftId, Guid? ShiftPatternId, ShiftPatternDayType? PatternDayType, RosterAssignmentSource Source, bool IsCalendarOverride, string Message);
public sealed class RosterAssignmentRequest { public List<Guid> EmployeeIds { get; set; } = []; public DateOnly FromDate { get; set; } public DateOnly ToDate { get; set; } public Guid? ShiftId { get; set; } public RosterDayType DayType { get; set; } = RosterDayType.Shift; public string? Reason { get; set; } }
public sealed record RosterDayDto(Guid Id, Guid EmployeeId, DateOnly RosterDate, Guid? ShiftId, Guid? ShiftPatternId, RosterDayType DayType, RosterAssignmentSource AssignmentSource, bool IsOverride, bool IsCalendarOverride, string? Comment);
public sealed record RosterUploadRowDto(int RowNumber, string EmployeeCode, DateOnly RosterDate, string? ShiftCode, RosterDayType DayType, bool IsValid, string? ErrorMessage);
public sealed record RosterUploadBatchDto(Guid Id, string FileName, RosterUploadStatus Status, int TotalRows, int ValidRows, int InvalidRows, int CommittedRows, IReadOnlyList<RosterUploadRowDto> Rows);
