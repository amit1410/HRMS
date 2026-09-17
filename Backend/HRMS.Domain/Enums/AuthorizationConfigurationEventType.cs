namespace HRMS.Domain.Enums;

public enum AuthorizationConfigurationEventType
{
    RolePermissionGranted = 0,
    RolePermissionRevoked = 1,
    RoleScopeAdded = 2,
    RoleScopeRemoved = 3,
    RoleScopeModeChanged = 4
}
