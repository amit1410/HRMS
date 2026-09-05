namespace HRMS.Domain.Enums;

public enum LeaveUnit { Day = 0, Hour = 1 }
public enum LeavePolicyVersionStatus { Draft = 0, Published = 1, Retired = 2 }
public enum EligibilityMode { Immediate = 0, MinimumService = 1 }
public enum EligibilityServiceUnit { Days = 0, Months = 1 }
public enum ProbationMode { Allowed = 0, NotAllowed = 1, AfterConfirmation = 2 }
public enum NoticePeriodMode { Allowed = 0, NotAllowed = 1, AllowedWithApproval = 2 }
public enum EntitlementMode { Allocated = 0, Unlimited = 1, NoBalanceRequired = 2 }
public enum EntitlementSource { PolicyAccrual = 0, ExternalGrant = 1, NoBalanceRequired = 2 }
public enum AccrualFrequency { None = 0, Upfront = 1, Monthly = 2, Quarterly = 3, SemiAnnual = 4, Annual = 5 }
public enum AccrualTiming { StartOfPeriod = 0, EndOfPeriod = 1 }
public enum PartialDayMode { FullDayOnly = 0, HalfDayAllowed = 1 }
public enum BackdatedRequestMode { NotAllowed = 0, Allowed = 1, AllowedUpToDays = 2 }
public enum RequestLimitPeriod { Month = 0, LeavePeriod = 1 }
public enum HolidayTreatment { Exclude = 0, Include = 1 }
public enum WeekOffTreatment { Exclude = 0, Include = 1 }
public enum SandwichMode { Disabled = 0, Holiday = 1, WeekOff = 2, HolidayAndWeekOff = 3 }
public enum AttachmentRequirement { None = 0, Optional = 1, Required = 2, RequiredAboveQuantity = 3 }
public enum ClubbingRelation { NotAllowed = 0 }
