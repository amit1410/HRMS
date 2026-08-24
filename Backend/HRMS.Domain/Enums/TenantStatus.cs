namespace HRMS.Domain.Enums;

/// <summary>Lifecycle state of a tenant (organization). Controls whether its users may sign in.</summary>
public enum TenantStatus
{
    Active = 1,
    Inactive = 2,
    Suspended = 3
}
