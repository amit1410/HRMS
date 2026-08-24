using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Sign-in behaviour: credential verification is scoped to the organization the request was addressed to,
/// failures are indistinguishable to a caller, and refresh tokens are stored in a non-recoverable form.
/// <para>
/// Every test says which address it is signing in at, because that is now the only thing that decides which
/// organization's credentials are checked — there is no field in the request for it.
/// </para>
/// </summary>
public class AuthServiceLoginTests
{
    private const string Password = SeedData.DefaultUserPassword;
    private const string Demo01 = AuthTestHarness.Demo01Host;
    private const string Demo02 = AuthTestHarness.Demo02Host;

    [Fact]
    public async Task Login_with_valid_credentials_returns_tokens_and_profile()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var result = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo01.com",
            Password = Password
        });

        Assert.True(result.Succeeded);
        Assert.Equal(ResultStatus.Success, result.Status);

        var response = result.Value!;
        Assert.False(string.IsNullOrWhiteSpace(response.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(response.RefreshToken));
        Assert.Equal("Bearer", response.TokenType);
        Assert.Equal(harness.JwtSettings.AccessTokenMinutes * 60, response.ExpiresInSeconds);

        // The organization code is still in the response and still in the token: it stopped being an input,
        // not an identifier.
        Assert.Equal(SeedData.TenantIds.Demo01, response.User.TenantId);
        Assert.Equal("DEMO01", response.User.TenantCode);
        Assert.Equal("admin@demo01.com", response.User.Email);
        Assert.Equal("Alice Admin", response.User.FullName);
        Assert.Equal(new[] { RoleNames.TenantAdmin }, response.User.Roles);
    }

    [Fact]
    public async Task Login_is_case_insensitive_on_the_email_and_trims_it()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var result = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = " Admin@Demo01.COM ",
            Password = Password
        });

        Assert.True(result.Succeeded);
        Assert.Equal(SeedData.TenantIds.Demo01, result.Value!.User.TenantId);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var result = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo01.com",
            Password = "not-the-password"
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// The central multi-tenant guarantee at sign-in: a valid credential pair for organization A must not
    /// authenticate at organization B's address, even though the email and password are entirely correct.
    /// This is the same guarantee as before, and it is now impossible to express any other way — there is no
    /// organization field left to get wrong, so the address is the whole of it.
    /// </summary>
    [Fact]
    public async Task Login_rejects_valid_credentials_presented_at_another_organizations_address()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var result = await harness.At(Demo02).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo01.com",
            Password = Password
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Null(result.Value);
    }

    /// <summary>
    /// Both demo organizations have an account per role, so the same local part exists twice. Signing in must
    /// return the user belonging to the organization whose address was used, never the other one's namesake.
    /// </summary>
    [Fact]
    public async Task Login_resolves_the_user_belonging_to_the_addressed_organization()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var demo01 = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "hr@demo01.com",
            Password = Password
        });

        var demo02 = await harness.At(Demo02).CreateService().LoginAsync(new LoginRequest
        {
            Email = "hr@demo02.com",
            Password = Password
        });

        Assert.True(demo01.Succeeded);
        Assert.True(demo02.Succeeded);
        Assert.Equal(SeedData.TenantIds.Demo01, demo01.Value!.User.TenantId);
        Assert.Equal(SeedData.TenantIds.Demo02, demo02.Value!.User.TenantId);
        Assert.Equal("Henry Human", demo01.Value.User.FullName);
        Assert.Equal("Hana Resource", demo02.Value.User.FullName);
        Assert.NotEqual(demo01.Value.User.Id, demo02.Value.User.Id);
    }

    /// <summary>
    /// The replacement for "unknown organization code is rejected". It is refused with its own message rather
    /// than the credentials one on purpose: the address is the organization, so a wrong address is not a
    /// wrong password, and telling someone their credentials failed would send them to re-type a password
    /// that was never the problem. It discloses nothing either — the request only arrived because DNS
    /// already resolved the host.
    /// </summary>
    [Fact]
    public async Task Login_at_an_address_that_resolves_to_no_organization_is_refused()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var result = await harness.AtAnUnknownHost().CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo01.com",
            Password = Password
        });

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Contains("no organization at this address", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An organization the catalog routes here but whose database was never provisioned. Our misconfiguration,
    /// so the caller gets the ordinary credentials message: there is nothing they could do with the detail,
    /// and naming it would describe our deployment to an anonymous caller.
    /// </summary>
    [Fact]
    public async Task Login_for_an_organization_missing_from_its_own_database_is_rejected_generically()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var unprovisioned = await harness.AtAnUnprovisionedOrganization().CreateService().LoginAsync(
            new LoginRequest { Email = "admin@demo01.com", Password = Password });

        var wrongPassword = await harness.At(Demo01).CreateService().LoginAsync(
            new LoginRequest { Email = "admin@demo01.com", Password = "wrong" });

        Assert.Equal(ResultStatus.Unauthorized, unprovisioned.Status);
        Assert.Equal(wrongPassword.Message, unprovisioned.Message);
    }

    /// <summary>
    /// A caller must not be able to tell "no such account" apart from "wrong password", so every
    /// pre-authentication failure at a real address returns one message. The unknown-address refusal is
    /// deliberately outside this set; see the test above for why that is not a regression.
    /// </summary>
    [Fact]
    public async Task Login_failures_are_indistinguishable_to_the_caller()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var unknownUser = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "nobody@demo01.com", Password = Password
        });
        var wrongPassword = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo01.com", Password = "wrong"
        });
        var wrongAddress = await harness.At(Demo02).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo01.com", Password = Password
        });

        var responses = new[] { unknownUser, wrongPassword, wrongAddress };
        Assert.All(responses, r => Assert.Equal(ResultStatus.Unauthorized, r.Status));
        Assert.Single(responses.Select(r => r.Message).Distinct());
    }

    [Fact]
    public async Task Login_is_refused_for_a_deactivated_account()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var arrange = harness.CreateUnscopedContext();
        var seeded = await arrange.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == "hr@demo01.com");
        seeded.IsActive = false;
        await arrange.SaveChangesAsync();

        var result = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "hr@demo01.com", Password = Password
        });

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Contains("deactivated", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Suspension in the organization's <em>own</em> database. Host resolution refuses a suspended
    /// organization at the edge, before this service is reached, so what this covers is the copy the sign-in
    /// would actually operate on having a status the cached catalog copy does not yet know about.
    /// </summary>
    [Fact]
    public async Task Login_is_refused_when_the_organization_is_suspended()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var arrange = harness.CreateUnscopedContext();
        var tenant = await arrange.Tenants.SingleAsync(t => t.TenantCode == "DEMO01");
        tenant.Status = TenantStatus.Suspended;
        await arrange.SaveChangesAsync();

        var result = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo01.com", Password = Password
        });

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Contains("suspended", result.Message, StringComparison.OrdinalIgnoreCase);

        // The other organization is unaffected.
        var other = await harness.At(Demo02).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo02.com", Password = Password
        });
        Assert.True(other.Succeeded);
    }

    [Fact]
    public async Task Login_records_the_last_login_time()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var before = harness.CreateUnscopedContext();
        var seeded = await before.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == "admin@demo01.com");
        Assert.Null(seeded.LastLoginDate);

        var result = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo01.com", Password = Password
        });
        Assert.True(result.Succeeded);

        var after = harness.CreateUnscopedContext();
        var reloaded = await after.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == "admin@demo01.com");
        Assert.NotNull(reloaded.LastLoginDate);
        Assert.Equal(reloaded.LastLoginDate, result.Value!.User.LastLoginDateUtc);
    }

    [Fact]
    public async Task Login_grants_exactly_the_permissions_of_the_assigned_role()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var admin = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo01.com", Password = Password
        });
        var hr = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "hr@demo01.com", Password = Password
        });

        // TenantAdmin is granted every permission; HRManager only the subset in the seed map.
        Assert.Equal(Permissions.All.Count, admin.Value!.User.Permissions.Count);
        Assert.Equal(
            SeedData.RolePermissionMap[RoleNames.HRManager].OrderBy(p => p).ToList(),
            hr.Value!.User.Permissions);
        Assert.DoesNotContain(Permissions.Employee.Delete, hr.Value.User.Permissions);
    }

    /// <summary>A stolen database must not yield usable refresh tokens.</summary>
    [Fact]
    public async Task Login_persists_only_a_hash_of_the_refresh_token()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var result = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo01.com", Password = Password
        });

        var issued = result.Value!.RefreshToken;

        var context = harness.CreateUnscopedContext();
        var stored = await context.RefreshTokens.IgnoreQueryFilters().SingleAsync();

        Assert.NotEqual(issued, stored.TokenHash);
        Assert.Equal(harness.TokenService.HashRefreshToken(issued), stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length); // SHA-256 rendered as hex
        Assert.Equal(SeedData.TenantIds.Demo01, stored.TenantId);
        Assert.Null(stored.RevokedAtUtc);
        Assert.True(stored.ExpiresAtUtc > DateTime.UtcNow.AddDays(harness.JwtSettings.RefreshTokenDays - 1));
    }

    [Fact]
    public async Task Login_stamps_the_refresh_token_with_the_users_own_tenant()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        await harness.At(Demo02).CreateService().LoginAsync(new LoginRequest
        {
            Email = "admin@demo02.com", Password = Password
        });

        var context = harness.CreateUnscopedContext();
        var stored = await context.RefreshTokens.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(SeedData.TenantIds.Demo02, stored.TenantId);
    }

    [Fact]
    public async Task Login_rejects_missing_credentials_without_throwing()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var result = await harness.At(Demo01).CreateService().LoginAsync(new LoginRequest
        {
            Email = null!, Password = null!
        });

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }
}
