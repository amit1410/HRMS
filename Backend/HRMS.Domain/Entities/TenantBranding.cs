namespace HRMS.Domain.Entities;

/// <summary>
/// The branding a tenant chooses to show on the sign-in screen: its name, logo, accent colour and
/// welcome message.
/// <para>
/// A 1:1 extension of <see cref="Tenant"/> rather than columns on <c>Tenants</c>. That keeps the root of
/// tenant isolation unchanged, and it lets presentation concerns grow — a background image, a favicon, a
/// second SSO provider — without touching the entity every isolation rule is keyed to.
/// </para>
/// <para>
/// Deliberately <b>not</b> a <c>BaseEntity</c> and <b>not</b> an <c>ITenantEntity</c>, mirroring
/// <see cref="Tenant"/> itself. Both of those would break the one thing this entity exists for: it is
/// read <i>before anyone signs in</i>, when there is no resolved tenant. A global query filter would
/// match no rows, and the SaveChanges tenant stamp would overwrite the key. The absence of both is the
/// design, not an oversight.
/// </para>
/// </summary>
public class TenantBranding
{
    /// <summary>Primary key and foreign key both — one branding row per tenant, or none.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Whether this tenant's branding may be served to an anonymous caller.
    /// <para>
    /// Defaults to <c>false</c>. What that default buys has changed: branding is now looked up by the host
    /// the request arrived at, so DNS has already confirmed the organization exists and the flag no longer
    /// protects against anyone discovering that. What it still governs is whether a visitor who is not
    /// signed in — anyone at all, including someone who guessed the address — is shown the organization's
    /// name, logo, colours and support contact. That is a disclosure an organization may reasonably not
    /// want, so the decision stays theirs and the default stays closed.
    /// </para>
    /// </summary>
    public bool IsPublic { get; set; }

    /// <summary>
    /// The name to greet users with, when it differs from the legal name in <see cref="Tenant.TenantName"/>
    /// (a trading name, or a shorter form that fits a heading). Null means use the tenant name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Absolute <c>https</c> URL of the logo. Anything else — <c>http</c>, a relative path, a
    /// <c>data:</c> or <c>javascript:</c> URI — is discarded when the branding is read, so a bad value
    /// here degrades to "no logo" rather than becoming a mixed-content warning or a script vector.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Accent colour as <c>#RRGGBB</c>. Validated on read before it can reach a CSS custom property:
    /// this string ends up inside a stylesheet, so it is treated as untrusted input even though it came
    /// from our own database.
    /// </summary>
    public string? PrimaryColor { get; set; }

    /// <summary>A short line shown above the sign-in form. Null means use the product's own wording.</summary>
    public string? WelcomeMessage { get; set; }

    /// <summary>
    /// Where users should turn when they cannot get in. Shown on the sign-in screen when present, which
    /// is why it should be a shared mailbox rather than a person: this is visible pre-authentication to
    /// anyone who reaches the organization's address.
    /// </summary>
    public string? SupportEmail { get; set; }

    /// <summary>
    /// Whether this tenant expects to sign in through an external identity provider.
    /// <para>
    /// The flag exists so the abstraction is real rather than imagined, but it grants nothing on its own:
    /// the client only offers single sign-on when the flag is set <i>and</i> a provider is actually
    /// implemented, and none is yet. Setting this to <c>true</c> today changes nothing a user can see.
    /// </para>
    /// </summary>
    public bool SsoEnabled { get; set; }

    /// <summary>The provider's display name ("Contoso ID"). A label only — it selects no code path.</summary>
    public string? SsoProviderName { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
}
