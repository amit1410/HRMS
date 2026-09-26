namespace HRMS.Domain.Enums;

public enum AttendanceDeviceStatus { Active, Inactive, Disabled }
public enum AttendanceDeviceConnectionMode { Push, Pull, FileImport, ManualApi }
public enum AttendanceDeviceMappingStatus { Active, Inactive }
public enum AttendanceDeviceSyncStatus { Running, Succeeded, PartiallySucceeded, Failed }
public enum AttendanceDeviceIngestionStatus { Accepted, Duplicate, Rejected, Unmapped, RequiresPeriodReopen }
public enum AttendanceDevicePunchDirection { Unknown, In, Out }
