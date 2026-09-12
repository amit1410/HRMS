namespace HRMS.Domain.Enums;

public enum AttendanceCaptureMode { BiometricOnly, SelfPunch, AutoLoginLogout, Manual, Mixed }
public enum ShiftType { Fixed, Flexible }
[Flags]
public enum AttendanceSource { Biometric = 1, Portal = 2, AutoLoginLogout = 4, Manual = 8 }
public enum PunchSource { Biometric, Portal, AutoLoginLogout, Manual }
public enum PunchDirection { In, Out }
public enum EmployeeAttendanceDayStatus { Present, Absent, OnLeave, Holiday, WeeklyOff, Incomplete, NotProcessed }
public enum PostShiftMarkOutMode { Allowed, AllowedWithinLimit, RequiresApprovalBeyondLimit, DisabledBeyondLimit }
public enum ShiftPatternDayType { Shift, WeeklyOff }
public enum RosterDayType { Shift, WeeklyOff, NonWorking, Holiday }
public enum RosterAssignmentSource { Auto, Manual, Upload, System, Override }
public enum RosterUploadStatus { Validating, Validated, Committed, Failed }
public enum RosterUploadAction { New, Update, Unchanged, Error }
public enum RosterCalendarDayType { Unknown, WorkingDay, Holiday, WeeklyOff }
public enum RosterChangeType { Created, Updated, Removed }
