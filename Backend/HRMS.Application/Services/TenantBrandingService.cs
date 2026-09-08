using System.Text.RegularExpressions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Tenants;
using HRMS.Domain.Enums;
using HRMS.Domain.Entities;
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
/// <b>Active tenants receive usable defaults.</b> Missing custom branding is filled from tenant metadata
/// and product defaults. Unknown, inactive, and unpublished workspaces return not-found so the client can
/// distinguish an invalid address from a valid tenant with optional branding fields.
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
    private const string DefaultPrimaryColor = "#1D4ED8";
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
            return Result<TenantBrandingDto>.NotFound("Workspace was not found.");
        }

        // Projecting the reference navigation makes this one statement with a left join: an organization with
        // no branding row comes back with Branding == null rather than not coming back at all, which keeps
        // "not opted in" and "no branding configured" on the same code path.
        var found = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == shard.TenantId)
            .Select(t => new { t.Status, t.TenantName, t.Email, t.Branding })
            .FirstOrDefaultAsync(cancellationToken);

        if (found is null || found.Status != TenantStatus.Active || found.Branding is { IsPublic: false })
        {
            // The status is re-read rather than taken from the descriptor because the descriptor is cached
            // for up to Sharding:CacheSeconds: an organization suspended moments ago still resolves, and
            // this is the fresher copy.
            return Result<TenantBrandingDto>.NotFound("Workspace was not found.");
        }

        var branding = found.Branding;

        return Result<TenantBrandingDto>.Success(new TenantBrandingDto(
            DisplayName: NullIfBlank(branding?.DisplayName) ?? NullIfBlank(found.TenantName) ?? "HRMS",
            LogoUrl: SafeLogoUrl(branding?.LogoUrl),
            PrimaryColor: SafeColor(branding?.PrimaryColor) ?? DefaultPrimaryColor,
            WelcomeMessage: NullIfBlank(branding?.WelcomeMessage),
            SupportEmail: NullIfBlank(branding?.SupportEmail) ?? NullIfBlank(found.Email),
            SsoEnabled: branding?.SsoEnabled ?? false,
            SsoProviderName: NullIfBlank(branding?.SsoProviderName),
            LoginIdentifierMode: branding?.LoginIdentifierMode ?? TenantLoginIdentifierMode.EmailOrEmployeeCode,
            PasswordRecoveryEnabled: branding?.PasswordRecoveryEnabled ?? true,
            AllowEmailOtp: branding?.AllowEmailOtp ?? true,
            AllowSmsOtp: branding?.AllowSmsOtp ?? true,
            OtpExpiryMinutes: branding?.OtpExpiryMinutes ?? 5,
            OtpMaxAttempts: branding?.OtpMaxAttempts ?? 5,
            OtpResendCooldownSeconds: branding?.OtpResendCooldownSeconds ?? 60,
            OtpMaxResends: branding?.OtpMaxResends ?? 5));
    }

    public async Task<Result<TenantLoginIdentifierMode>> GetLoginIdentifierModeAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await CurrentTenantAsync(cancellationToken);
        return tenant is null
            ? Result<TenantLoginIdentifierMode>.NotFound("Workspace was not found.")
            : Result<TenantLoginIdentifierMode>.Success(tenant.Branding?.LoginIdentifierMode ?? TenantLoginIdentifierMode.EmailOrEmployeeCode);
    }

    public async Task<Result<TenantLoginIdentifierMode>> SetLoginIdentifierModeAsync(TenantLoginIdentifierMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode)) return Result<TenantLoginIdentifierMode>.Invalid("loginIdentifierMode", "Choose a supported login identifier mode.");
        var tenant = await CurrentTenantAsync(cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active) return Result<TenantLoginIdentifierMode>.NotFound("Workspace was not found.");
        var branding = tenant.Branding ?? new TenantBranding { TenantId = tenant.Id, IsPublic = true };
        branding.LoginIdentifierMode = mode;
        if (tenant.Branding is null) _db.TenantBranding.Add(branding);
        await _db.SaveChangesAsync(cancellationToken);
        return Result<TenantLoginIdentifierMode>.Success(mode, "Login identifier setting saved.");
    }

    public async Task<Result<TenantRecoverySettingsDto>> GetRecoverySettingsAsync(CancellationToken cancellationToken = default)
    {
        var tenant = await CurrentTenantAsync(cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active) return Result<TenantRecoverySettingsDto>.NotFound("Workspace was not found.");
        var b = tenant.Branding;
        return Result<TenantRecoverySettingsDto>.Success(new(b?.PasswordRecoveryEnabled ?? true, b?.AllowEmailOtp ?? true, b?.AllowSmsOtp ?? true, b?.OtpExpiryMinutes ?? 5, b?.OtpMaxAttempts ?? 5, b?.OtpResendCooldownSeconds ?? 60, b?.OtpMaxResends ?? 5));
    }

    public async Task<Result<TenantRecoverySettingsDto>> SetRecoverySettingsAsync(UpdateTenantRecoverySettingsRequest request, CancellationToken cancellationToken = default)
    {
        if (request.OtpExpiryMinutes is < 1 or > 60 || request.OtpMaxAttempts is < 1 or > 10 || request.OtpResendCooldownSeconds is < 10 or > 3600 || request.OtpMaxResends is < 1 or > 10)
            return Result<TenantRecoverySettingsDto>.Invalid("Recovery settings are outside the allowed range.");
        var tenant = await CurrentTenantAsync(cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.Active) return Result<TenantRecoverySettingsDto>.NotFound("Workspace was not found.");
        var b = tenant.Branding ?? new TenantBranding { TenantId = tenant.Id, IsPublic = true };
        b.PasswordRecoveryEnabled = request.PasswordRecoveryEnabled; b.AllowEmailOtp = request.AllowEmailOtp; b.AllowSmsOtp = request.AllowSmsOtp;
        b.OtpExpiryMinutes = request.OtpExpiryMinutes; b.OtpMaxAttempts = request.OtpMaxAttempts; b.OtpResendCooldownSeconds = request.OtpResendCooldownSeconds; b.OtpMaxResends = request.OtpMaxResends;
        if (tenant.Branding is null) _db.TenantBranding.Add(b);
        await _db.SaveChangesAsync(cancellationToken);
        return Result<TenantRecoverySettingsDto>.Success(new(b.PasswordRecoveryEnabled, b.AllowEmailOtp, b.AllowSmsOtp, b.OtpExpiryMinutes, b.OtpMaxAttempts, b.OtpResendCooldownSeconds, b.OtpMaxResends), "Password recovery settings saved.");
    }

    private async Task<Tenant?> CurrentTenantAsync(CancellationToken cancellationToken)
    {
        if (_shardContext.Current is not ShardDescriptor shard) return null;
        return await _db.Tenants.Include(t => t.Branding).FirstOrDefaultAsync(t => t.Id == shard.TenantId, cancellationToken);
    }

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
