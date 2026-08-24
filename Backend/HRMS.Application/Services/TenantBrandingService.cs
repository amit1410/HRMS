using System.Text.RegularExpressions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Tenants;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Serves the branding a sign-in screen shows for the organization at the address the request arrived at.
/// <para>
/// This is the only read in the application that answers an unauthenticated caller with tenant data, so
/// three rules shape the whole class.
/// </para>
/// <para>
/// <b>The caller does not choose the organization.</b> There is no code, no identifier and no path
/// parameter — only <see cref="IShardContext"/>, filled in from the host by the resolution middleware. A
/// visitor can therefore only ever ask about the organization whose address they are already at, which is
/// what removes this endpoint's ability to be walked through candidate organizations at all.
/// </para>
/// <para>
/// <b>It never reports a failure.</b> No organization at this address, not active, and not opted in all
/// return <see cref="TenantBrandingDto.Neutral"/> with a success status. <see cref="AuthService"/> makes
/// the same choice for the same reason, and this endpoint would undo it by being more forthcoming than
/// the login it sits in front of.
/// </para>
/// <para>
/// <b>The two answers that must be indistinguishable cost the same.</b> "Opted in" and "exists but is
/// suspended or has not opted in" both run exactly one query, so they cannot be told apart by timing
/// either — the property <c>VerifyAgainstDummyHash</c> buys for sign-in, kept here rather than given back.
/// An address that resolves to nothing runs no query and is measurably faster, which is deliberate and
/// costs nothing: the middleware has already answered that question, and DNS answered it before that.
/// </para>
/// <para>
/// It reads the catalog rather than a tenant's own database, because it has to: the caller is anonymous, so
/// nothing has authenticated them into the organization whose branding they are being shown. That is also
/// why this is one of the very few services allowed to touch <see cref="IHrmsCatalogDbContext"/>.
/// </para>
/// </summary>
public class TenantBrandingService : ITenantBrandingService
{
    /// <summary>
    /// Exactly <c>#RRGGBB</c>. Anything else is discarded rather than passed on: the value ends up inside
    /// a stylesheet on the client, so it is treated as untrusted even though it came from our database.
    /// An administrator with a typo gets the default accent, not a broken page.
    /// </summary>
    private static readonly Regex HexColorPattern =
        new("^#[0-9A-Fa-f]{6}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IHrmsCatalogDbContext _db;
    private readonly IShardContext _shardContext;

    public TenantBrandingService(IHrmsCatalogDbContext db, IShardContext shardContext)
    {
        _db = db;
        _shardContext = shardContext;
    }

    public async Task<Result<TenantBrandingDto>> GetForCurrentOrganizationAsync(
        CancellationToken cancellationToken = default)
    {
        // The apex host, or an address nobody has registered. There is no organization to brand as, and
        // nothing a caller could add to the request to name one.
        if (_shardContext.Current is not ShardDescriptor shard)
        {
            return Neutral;
        }

        // Projecting the reference navigation makes this one statement with a left join: an organization with
        // no branding row comes back with Branding == null rather than not coming back at all, which keeps
        // "not opted in" and "no branding configured" on the same code path.
        var found = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == shard.TenantId)
            .Select(t => new { t.Status, t.Branding })
            .FirstOrDefaultAsync(cancellationToken);

        if (found?.Branding is null || found.Status != TenantStatus.Active || !found.Branding.IsPublic)
        {
            // Three different situations, deliberately one answer. The status is re-read rather than taken
            // from the descriptor because the descriptor is cached for up to Sharding:CacheSeconds: an
            // organization suspended moments ago still resolves, and this is the fresher copy.
            return Neutral;
        }

        var branding = found.Branding;

        return Result<TenantBrandingDto>.Success(new TenantBrandingDto(
            DisplayName: NullIfBlank(branding.DisplayName),
            LogoUrl: SafeLogoUrl(branding.LogoUrl),
            PrimaryColor: SafeColor(branding.PrimaryColor),
            WelcomeMessage: NullIfBlank(branding.WelcomeMessage),
            SupportEmail: NullIfBlank(branding.SupportEmail),
            SsoEnabled: branding.SsoEnabled,
            SsoProviderName: NullIfBlank(branding.SsoProviderName)));
    }

    private static Result<TenantBrandingDto> Neutral =>
        Result<TenantBrandingDto>.Success(TenantBrandingDto.Neutral);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// An absolute <c>https</c> URL, or null. <c>http</c> would be mixed content on a secure page, and the
    /// schemes that matter more — <c>javascript:</c>, <c>data:</c> — are refused here rather than left for
    /// the client to notice.
    /// </summary>
    private static string? SafeLogoUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? trimmed
            : null;
    }

    private static string? SafeColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return HexColorPattern.IsMatch(trimmed) ? trimmed : null;
    }
}
