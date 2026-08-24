namespace HRMS.Application.Security;

/// <summary>
/// Custom JWT claim type names used across the app. Defined once so the token service (which writes
/// them) and the tenant context / authorization policies (which read them) can never drift apart.
/// Short names are used deliberately to keep the token small, and inbound claim mapping is disabled in
/// the API so these arrive exactly as written.
/// </summary>
public static class HrmsClaimTypes
{
    public const string UserId = "uid";
    public const string TenantId = "tid";
    public const string TenantCode = "tcode";
    public const string Email = "email";
    public const string Permission = "permission";
    public const string Role = "role";
    public const string FirstName = "given_name";
    public const string LastName = "family_name";
}
