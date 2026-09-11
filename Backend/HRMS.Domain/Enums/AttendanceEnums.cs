namespace HRMS.Domain.Enums;

public enum AttendanceCaptureMode { BiometricOnly, SelfPunch, AutoLoginLogout, Manual, Mixed }
public enum ShiftPatternDayType { Shift, WeeklyOff }
public enum RosterDayType { Shift, WeeklyOff, NonWorking }
public enum RosterAssignmentSource { Auto, Manual, Upload, System, Override }
public enum RosterUploadStatus { Validating, Validated, Committed, Failed }
