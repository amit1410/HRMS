namespace HRMS.Domain.Enums;

public enum ClearanceOwnerType { Role, Department, SpecificUser, Manager, Hr, Accounts, It, Admin }
public enum SeparationClearanceStatus { NotStarted, InProgress, ReadyForCompletion, Completed, Reopened, Cancelled }
public enum SeparationClearanceTaskStatus { Pending, InProgress, Cleared, Blocked, Rejected, Waived, NotApplicable }
public enum SeparationClearanceTaskCategory { Manager, Hr, It, Admin, Finance, Accounts, Security, Facilities, Asset, Other }
public enum SeparationAssetReturnStatus { PendingReturn, Returned, Damaged, Lost, NotApplicable, RecoveryRequired }
public enum SeparationAssetCondition { Unknown, Good, Damaged, Lost }
public enum SeparationClearanceEventType { ClearanceStarted, TaskAssigned, TaskStarted, TaskCleared, TaskBlocked, TaskWaived, AssetReturned, AssetMarkedDamaged, AssetMarkedLost, ClearanceCompleted, ClearanceReopened, LwdScheduleUpdated }
