using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Tenants;

public sealed record TenantRecoverySettingsDto(bool PasswordRecoveryEnabled, bool AllowEmailOtp, bool AllowSmsOtp, int OtpExpiryMinutes, int OtpMaxAttempts, int OtpResendCooldownSeconds, int OtpMaxResends);
public sealed record UpdateTenantRecoverySettingsRequest(bool PasswordRecoveryEnabled, bool AllowEmailOtp, bool AllowSmsOtp, int OtpExpiryMinutes, int OtpMaxAttempts, int OtpResendCooldownSeconds, int OtpMaxResends);

/// <summary>
/// The branding a sign-in screen may show for the organization at the address the request arrived at,
/// before anyone has authenticated.
/// <para>
/// Custom fields are optional. Active organizations still receive a usable display name and accent color
/// from the service when custom branding is absent; unknown, inactive, and unpublished organizations fail
/// with a not-found result.
/// </para>
/// <para>
/// There is no organization identifier here, not even an echoed one. The caller supplied no identifier —
/// the host decided — so there is nothing to echo back, and filling one in from the resolved organization
/// would hand an anonymous visitor the internal code of an organization that has opted out of showing
/// them anything at all.
/// </para>
/// </summary>
/// <param name="DisplayName">The organization's name, including the tenant-name fallback.</param>
/// <param name="LogoUrl">An absolute <c>https</c> logo URL. Never any other scheme — see the service.</param>
/// <param name="PrimaryColor">An accent colour as <c>#RRGGBB</c>. Never any other shape — see the service.</param>
/// <param name="WelcomeMessage">A short line to show above the form.</param>
/// <param name="SupportEmail">Who to contact for help getting in.</param>
/// <param name="SsoEnabled">
/// Whether this organization expects single sign-on. False for everyone today; a client must still have
/// an implemented provider before it offers anything, so this flag alone can never produce a sign-in
/// route that does not work.
/// </param>
/// <param name="SsoProviderName">The provider's display label, when there is one.</param>
public record TenantBrandingDto(
    string? DisplayName,
    string? LogoUrl,
    string? PrimaryColor,
    string? WelcomeMessage,
    string? SupportEmail,
    bool SsoEnabled,
    string? SsoProviderName,
    TenantLoginIdentifierMode LoginIdentifierMode,
    bool PasswordRecoveryEnabled = true,
    bool AllowEmailOtp = true,
    bool AllowSmsOtp = true,
    int OtpExpiryMinutes = 5,
    int OtpMaxAttempts = 5,
    int OtpResendCooldownSeconds = 60,
    int OtpMaxResends = 5)
{
    /// <summary>
    /// Retained for compatibility with provider/client code that models an empty branding payload. The
    /// current active-tenant service path returns fallback branding instead of using this value.
    /// </summary>
    public static TenantBrandingDto Neutral { get; } = new(null, null, null, null, null, false, null, TenantLoginIdentifierMode.EmailOrEmployeeCode);
}
