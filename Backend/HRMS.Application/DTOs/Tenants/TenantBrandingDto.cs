namespace HRMS.Application.DTOs.Tenants;

/// <summary>
/// The branding a sign-in screen may show for the organization at the address the request arrived at,
/// before anyone has authenticated.
/// <para>
/// Every field is optional, and that is what makes this DTO safe to return to anonymous callers: an
/// address no organization uses, one whose organization is not active, and one whose organization has not
/// opted in all produce the same all-null response. A caller cannot tell the three apart.
/// </para>
/// <para>
/// There is no organization identifier here, not even an echoed one. The caller supplied no identifier —
/// the host decided — so there is nothing to echo back, and filling one in from the resolved organization
/// would hand an anonymous visitor the internal code of an organization that has opted out of showing
/// them anything at all.
/// </para>
/// </summary>
/// <param name="DisplayName">The organization's name, or null when there is no branding to show.</param>
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
    string? SsoProviderName)
{
    /// <summary>
    /// The response for "there is nothing to show" — whatever the reason. Used for an address that resolves
    /// to no organization, an inactive organization, and one that has not made its branding public, so that
    /// all three are one indistinguishable answer rather than three distinguishable ones.
    /// <para>
    /// A single shared instance rather than a factory: it takes no input now, and one instance makes it
    /// impossible for two callers to produce responses that differ in any way.
    /// </para>
    /// </summary>
    public static TenantBrandingDto Neutral { get; } = new(null, null, null, null, null, false, null);
}
