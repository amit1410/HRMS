namespace HRMS.Domain.Authorization;

/// <summary>Permissions owned by the catalog-backed platform identity domain.</summary>
public static class PlatformPermissions
{
    public const string TenantView = "PlatformTenant.View";
    public const string TenantCreate = "PlatformTenant.Create";
    public const string TenantUpdateStatus = "PlatformTenant.UpdateStatus";

    public static readonly IReadOnlyList<string> All =
    [TenantView, TenantCreate, TenantUpdateStatus];
}
