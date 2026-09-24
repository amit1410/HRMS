namespace HRMS.Domain.Enums;

public enum SeparationType { EmployeeInitiated, EmployerInitiated }
public enum SeparationReasonCategory { Resignation, Retirement, Termination, Absconding, Death, ContractEnd, MutualSeparation, Redundancy, Other }
public enum EmployeeSeparationStatus { Draft, Submitted, ManagerReview, HrReview, Approved, Rejected, Withdrawn, NoticePeriod, ReadyForExit, Exited, Cancelled }
public enum EmployeeSeparationInitiator { Employee, Manager, Hr, System }
public enum NoticeDisposition { None, Waived, Recoverable, EmployeeBuyoutRequested, CompanyWaived }
public enum EmployeeSeparationEventType { Created, Submitted, ManagerApproved, ManagerRejected, HrApproved, HrRejected, Rejected, Withdrawn, Approved, LwdRevised, LwdChanged, NoticePeriodActivated, NoticeStarted, Cancelled, NoticeRequirementSnapshotted, NoticeWaiverApplied, NoticeWaiverRevised, ApprovedLwdRevised, ApprovedLwdExtended, ApprovedLwdReduced, NoticePeriodRecalculated }
