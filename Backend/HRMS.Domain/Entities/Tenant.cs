using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

/// <summary>
/// An organization/company using the HRMS. This is the root of tenant isolation:
/// every <see cref="ITenantEntity"/> row is scoped to a <see cref="Tenant"/> via its Id.
/// The Tenant entity itself is NOT tenant-scoped (there is no global query filter on it).
/// <para>
/// Mapped by <em>two</em> contexts, because it answers two different questions. In the catalog database
/// it is the routing table — every organization, one row each, resolved by <see cref="Host"/> before any
/// connection to tenant data exists. In a tenant's own shard database it appears exactly once, as the
/// principal row the four <c>FK_*_Tenants_TenantId</c> foreign keys point at, which is what keeps
/// referential integrity inside a shard and lets ordinary queries read <c>Tenants</c> unchanged.
/// </para>
/// <para>
/// The catalog copy is authoritative for routing (<see cref="Host"/>, <see cref="ShardKey"/>,
/// <see cref="Status"/>). The shard copy exists for integrity and display. Provisioning is the only
/// writer of either, so the two cannot drift apart on their own.
/// </para>
/// </summary>
public class Tenant : BaseEntity
{
    /// <summary>Short, human-friendly unique code for an organization (e.g. "DEMO01").</summary>
    /// <remarks>
    /// No longer typed by anyone at sign-in — the host does that job now (see <see cref="Host"/>). It
    /// survives as the operator-facing label: it is what appears in logs, support conversations and the
    /// <c>tcode</c> token claim, and it is short and case-insensitive in a way a hostname is not.
    /// </remarks>
    public string TenantCode { get; set; } = string.Empty;

    /// <summary>
    /// The fully-qualified host this organization signs in at, lowercase — <c>"demo01.hrms.com"</c>.
    /// Globally unique, and the key tenant resolution actually uses.
    /// <para>
    /// Deliberately the <em>whole</em> host rather than just a subdomain label. Matching a complete host
    /// against a unique column is a single exact lookup with no parsing, and it makes a vanity domain
    /// (<c>"hr.acme.com"</c>) exactly as expressible as a whitelabel subdomain — one mechanism, not two.
    /// </para>
    /// <para>
    /// Not derived from <see cref="TenantCode"/>, which is capped at 20 characters, stored uppercase and
    /// unconstrained by DNS rules. Deriving one from the other would bake all three of those mismatches
    /// into the routing layer.
    /// </para>
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Identifies the database holding this organization's data. Unique, lowercase.
    /// <para>
    /// A key, <b>never a connection string</b>. The credentials live in configuration and the key is
    /// substituted into a template, so the catalog can be read, backed up or accidentally exposed without
    /// handing anyone a way to connect.
    /// </para>
    /// </summary>
    public string ShardKey { get; set; } = string.Empty;

    public string TenantName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public TenantStatus Status { get; set; } = TenantStatus.Active;

    // Navigation
    /// <summary>
    /// The organization's users. Mapped only by the shard context: users live in the tenant's own
    /// database, so the catalog ignores this navigation rather than dragging the whole entity graph
    /// into the routing database.
    /// </summary>
    public ICollection<User> Users { get; set; } = new List<User>();

    /// <summary>
    /// Optional sign-in branding. Null for a tenant that has never configured any, which is the normal
    /// case and the reason the sign-in screen always has a product-branded fallback.
    /// <para>
    /// Mapped only by the catalog context — the mirror image of <see cref="Users"/>. Branding is read to
    /// draw the sign-in screen, before any token exists and therefore before there is a shard to read it
    /// from, so it has to live beside the routing table. The shard context ignores this navigation.
    /// </para>
    /// </summary>
    public TenantBranding? Branding { get; set; }
}
