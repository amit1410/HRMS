using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Tenants;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Sharding;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Pre-authentication branding: what an anonymous visitor at an organization's address is shown, and — more
/// importantly — what they are not.
/// <para>
/// Nothing here passes an organization in. The service has no parameter for one, so every test states an
/// address instead, and the one thing that can go wrong is the service answering for an organization the
/// visitor did not arrive at.
/// </para>
/// </summary>
public class TenantBrandingServiceTests
{
    [Fact]
    public async Task Branding_is_returned_for_an_organization_that_has_opted_in()
    {
        using var database = await CreateDatabaseAsync();

        var result = await ReadAsync(database, TestShards.For(TestShards.Demo01Host));

        Assert.True(result.Succeeded);
        var branding = result.Value!;
        Assert.Equal("Demo Organization", branding.DisplayName);
        Assert.Equal("#0F766E", branding.PrimaryColor);
        Assert.Equal("Sign in to the Demo Organization workspace.", branding.WelcomeMessage);
        Assert.Equal("itsupport@demo01.com", branding.SupportEmail);

        // No logo is seeded, and single sign-on has no implemented provider.
        Assert.Null(branding.LogoUrl);
        Assert.False(branding.SsoEnabled);
        Assert.Null(branding.SsoProviderName);
    }

    /// <summary>
    /// The address, and nothing else, decides whose branding is served. Two organizations differ in every
    /// visible field, so serving the wrong one cannot look like success.
    /// </summary>
    [Fact]
    public async Task Each_address_is_served_its_own_organizations_branding()
    {
        using var database = await CreateDatabaseAsync();

        var demo01 = await ReadAsync(database, TestShards.For(TestShards.Demo01Host));
        var demo02 = await ReadAsync(database, TestShards.For(TestShards.Demo02Host));

        Assert.Equal("Demo Organization", demo01.Value!.DisplayName);
        Assert.Equal("Sample Organization", demo02.Value!.DisplayName);
        Assert.NotEqual(demo01.Value.PrimaryColor, demo02.Value.PrimaryColor);
    }

    /// <summary>
    /// The apex host, or an address nobody has registered. Succeeds with nothing to show rather than
    /// reporting a failure — a 404 here would be a way to ask whether an address belongs to a customer.
    /// </summary>
    [Fact]
    public async Task An_address_that_resolves_to_no_organization_gets_the_neutral_response()
    {
        using var database = await CreateDatabaseAsync();

        var result = await ReadAsync(database, shard: null);

        Assert.True(result.Succeeded);
        Assert.Same(TenantBrandingDto.Neutral, result.Value);
    }

    /// <summary>
    /// The opt-in is what it claims to be: an organization that has not published its branding is served the
    /// same response as an address that belongs to nobody. Asserted by equality against that response rather
    /// than field by field, so a field added later is covered without anyone remembering to add it here.
    /// </summary>
    [Fact]
    public async Task An_organization_that_has_not_opted_in_is_indistinguishable_from_an_unknown_address()
    {
        using var database = await CreateDatabaseAsync();
        await EditBrandingAsync(database, TestShards.Demo01Host, branding => branding.IsPublic = false);

        var optedOut = await ReadAsync(database, TestShards.For(TestShards.Demo01Host));
        var unknown = await ReadAsync(database, shard: null);

        Assert.True(optedOut.Succeeded);
        Assert.Equal(unknown.Value, optedOut.Value);
    }

    /// <summary>
    /// A suspended organization is hidden too. Reachable despite the middleware refusing suspended hosts at
    /// the edge, because the descriptor is cached for up to <c>Sharding:CacheSeconds</c> — for that window an
    /// organization suspended moments ago still resolves, and this read is what decides what it discloses.
    /// </summary>
    [Fact]
    public async Task A_suspended_organization_is_indistinguishable_too()
    {
        using var database = await CreateDatabaseAsync();

        using (var catalog = database.CreateCatalogContext())
        {
            var tenant = await catalog.Tenants.SingleAsync(t => t.Host == TestShards.Demo01Host);
            tenant.Status = TenantStatus.Suspended;
            await catalog.SaveChangesAsync();
        }

        // The descriptor still says Active, which is exactly the stale state being covered.
        var suspended = await ReadAsync(database, TestShards.For(TestShards.Demo01Host));

        Assert.True(suspended.Succeeded);
        Assert.Equal(TenantBrandingDto.Neutral, suspended.Value);
    }

    [Fact]
    public async Task An_organization_with_no_branding_row_gets_the_neutral_response()
    {
        using var database = await CreateDatabaseAsync();

        using (var catalog = database.CreateCatalogContext())
        {
            var branding = await catalog.TenantBranding
                .SingleAsync(b => b.Tenant!.Host == TestShards.Demo01Host);
            catalog.TenantBranding.Remove(branding);
            await catalog.SaveChangesAsync();
        }

        var result = await ReadAsync(database, TestShards.For(TestShards.Demo01Host));

        Assert.True(result.Succeeded);
        Assert.Equal(TenantBrandingDto.Neutral, result.Value);
    }

    /// <summary>
    /// A catalog that routes an address to an organization it holds no row for. Nothing to show, and nothing
    /// said about it — the same answer as every other reason for having nothing to show.
    /// </summary>
    [Fact]
    public async Task An_organization_missing_from_the_catalog_gets_the_neutral_response()
    {
        using var database = await CreateDatabaseAsync();

        var result = await ReadAsync(database, TestShards.Unprovisioned);

        Assert.True(result.Succeeded);
        Assert.Equal(TenantBrandingDto.Neutral, result.Value);
    }

    /// <summary>
    /// The accent colour reaches a CSS custom property on the client, so it is validated on the way out even
    /// though it came from our own database — an administrator with a text field is not a trusted source of
    /// stylesheet fragments. A bad value degrades to the default accent; the rest of the branding survives.
    /// <para>
    /// The column is <c>HasMaxLength(7)</c>, so the long injection case below could only be stored by a
    /// provider that does not enforce that or a migration that widens it. It is covered anyway: the read
    /// path's guarantee should not quietly depend on a column width that a later schema change could relax.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("red")]
    [InlineData("#0F766")]
    [InlineData("#0F766EE")]
    [InlineData("0F766E")]
    [InlineData("#0F766E; background-image: url(https://evil.example/beacon.png)")]
    [InlineData("var(--anything)")]
    [InlineData("  ")]
    public async Task A_colour_that_is_not_exactly_six_hex_digits_is_dropped(string stored)
    {
        using var database = await CreateDatabaseAsync();
        await EditBrandingAsync(database, TestShards.Demo01Host, b => b.PrimaryColor = stored);

        var result = await ReadAsync(database, TestShards.For(TestShards.Demo01Host));

        Assert.Null(result.Value!.PrimaryColor);
        Assert.Equal("Demo Organization", result.Value.DisplayName);
    }

    /// <summary>
    /// The logo is rendered as an image source, so only absolute <c>https</c> is served. <c>http</c> would be
    /// mixed content; the rest are script and exfiltration vectors that must not reach the client to be
    /// refused there.
    /// </summary>
    [Theory]
    [InlineData("http://cdn.example.com/logo.png")]
    [InlineData("//cdn.example.com/logo.png")]
    [InlineData("/assets/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zy8+")]
    [InlineData("file:///c:/windows/logo.png")]
    public async Task A_logo_url_that_is_not_absolute_https_is_dropped(string stored)
    {
        using var database = await CreateDatabaseAsync();
        await EditBrandingAsync(database, TestShards.Demo01Host, b => b.LogoUrl = stored);

        var result = await ReadAsync(database, TestShards.For(TestShards.Demo01Host));

        Assert.Null(result.Value!.LogoUrl);
    }

    [Fact]
    public async Task An_absolute_https_logo_url_is_served_and_trimmed()
    {
        using var database = await CreateDatabaseAsync();
        await EditBrandingAsync(
            database, TestShards.Demo01Host, b => b.LogoUrl = "  https://cdn.example.com/logo.png  ");

        var result = await ReadAsync(database, TestShards.For(TestShards.Demo01Host));

        Assert.Equal("https://cdn.example.com/logo.png", result.Value!.LogoUrl);
    }

    [Fact]
    public async Task Blank_text_fields_are_returned_as_null_rather_than_as_empty_strings()
    {
        using var database = await CreateDatabaseAsync();
        await EditBrandingAsync(database, TestShards.Demo01Host, b =>
        {
            b.DisplayName = "   ";
            b.WelcomeMessage = "";
            b.SupportEmail = "\t";
            b.SsoProviderName = " ";
        });

        var result = await ReadAsync(database, TestShards.For(TestShards.Demo01Host));

        Assert.Null(result.Value!.DisplayName);
        Assert.Null(result.Value.WelcomeMessage);
        Assert.Null(result.Value.SupportEmail);
        Assert.Null(result.Value.SsoProviderName);
    }

    /// <summary>
    /// The response carries no organization identifier, and this is a guard against putting one back. The
    /// caller supplied none — the host decided — so an echoed code or id would be new information handed to
    /// an anonymous visitor, including one visiting an organization that has opted out of telling them
    /// anything. It would also make the neutral response distinguishable from a real one at a glance.
    /// </summary>
    [Fact]
    public void The_response_shape_names_no_organization()
    {
        var forbidden = new[] { "tenantcode", "tenantid", "tenantname", "host", "shardkey", "id" };

        var offenders = typeof(TenantBrandingDto)
            .GetProperties()
            .Select(p => p.Name)
            .Where(name => forbidden.Contains(name.ToLowerInvariant()))
            .ToList();

        Assert.Empty(offenders);
    }

    private static async Task<SqliteInMemoryDatabase> CreateDatabaseAsync()
    {
        var database = new SqliteInMemoryDatabase();
        await database.SeedAsync();
        return database;
    }

    /// <summary>
    /// Reads branding as a request addressed to <paramref name="shard"/> would — a real
    /// <see cref="ShardContext"/> rather than a stand-in, since a stand-in would be free to permit the
    /// mid-scope switch production refuses. <c>null</c> is an address that resolves to no organization.
    /// </summary>
    private static async Task<Result<TenantBrandingDto>> ReadAsync(
        SqliteInMemoryDatabase database, ShardDescriptor? shard)
    {
        using var catalog = database.CreateCatalogContext();

        var shardContext = new ShardContext();
        if (shard is not null)
        {
            shardContext.Use(shard);
        }

        return await new TenantBrandingService(catalog, shardContext).GetForCurrentOrganizationAsync();
    }

    private static async Task EditBrandingAsync(
        SqliteInMemoryDatabase database, string host, Action<TenantBranding> edit)
    {
        using var catalog = database.CreateCatalogContext();

        var branding = await catalog.TenantBranding.SingleAsync(b => b.Tenant!.Host == host);
        edit(branding);
        await catalog.SaveChangesAsync();
    }
}
