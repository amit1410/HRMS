namespace HRMS.Domain.Enums;

public enum UserRoleAssignmentEventType
{
    Assigned = 0,
    EffectiveDatesChanged = 1,
    Revoked = 2,
    ScopeAdded = 3,
    ScopeRemoved = 4
}
