namespace HRMS.Domain.Authorization;

/// <summary>
/// Canonical role names. These are shared reference data seeded into the Role table and referenced
/// by authorization policies. Kept as constants to avoid magic strings across the codebase.
/// </summary>
public static class RoleNames
{
    public const string SuperAdmin = "SuperAdmin";
    public const string TenantAdmin = "TenantAdmin";
    public const string HRAdmin = "HRAdmin";
    public const string HRManager = "HRManager";
    public const string Manager = "Manager";
    public const string Employee = "Employee";
    public const string AccountLinkAdministrator = "AccountLinkAdministrator";
    public const string AccountLinkAuditor = "AccountLinkAuditor";

    public static readonly IReadOnlyList<string> All = new[]
    {
        SuperAdmin, TenantAdmin, HRAdmin, HRManager, Manager, Employee,
        AccountLinkAdministrator, AccountLinkAuditor
    };
}
