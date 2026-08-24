using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Refresh-token rotation, replay detection and sign-out — including the tenant boundary on both, since
/// refresh runs before any tenant is authenticated and therefore reads across tenants by necessity.
/// <para>
/// Refresh is addressed the same way sign-in is: at the organization's own host. That is what makes the
/// token/address agreement check reachable, and it is how a browser at <c>demo01.…</c> refreshes anyway.
/// </para>
/// </summary>
public class AuthServiceRefreshTests
{
    private const string Password = SeedData.DefaultUserPassword;
    private const string Demo01 = AuthTestHarness.Demo01Host;
    private const string Demo02 = AuthTestHarness.Demo02Host;

    [Fact]
    public async Task Refresh_issues_a_new_pair_and_consumes_the_presented_token()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var login = await SignInAsync(harness, Demo01, "admin@demo01.com");

        var refreshed = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        Assert.True(refreshed.Succeeded);
        var pair = refreshed.Value!;
        Assert.NotEqual(login.RefreshToken, pair.RefreshToken);
        Assert.Equal(SeedData.TenantIds.Demo01, pair.User.TenantId);
        Assert.Equal(new[] { RoleNames.TenantAdmin }, pair.User.Roles);

        var context = harness.CreateUnscopedContext();
        var old = await FindTokenAsync(context, harness, login.RefreshToken);
        var replacement = await FindTokenAsync(context, harness, pair.RefreshToken);

        Assert.NotNull(old.RevokedAtUtc);
        Assert.Equal(replacement.TokenHash, old.ReplacedByTokenHash);
        Assert.Null(replacement.RevokedAtUtc);
        Assert.Equal(old.TenantId, replacement.TenantId);
        Assert.Equal(old.UserId, replacement.UserId);
    }

    /// <summary>
    /// Replaying a rotated token means either a buggy client or a stolen token. Because the two cannot be
    /// told apart, every live session for that user is dropped.
    /// </summary>
    [Fact]
    public async Task Refresh_replay_is_rejected_and_revokes_every_live_session()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var firstSession = await SignInAsync(harness, Demo01, "admin@demo01.com");
        var secondSession = await SignInAsync(harness, Demo01, "admin@demo01.com");

        var rotated = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = firstSession.RefreshToken });
        Assert.True(rotated.Succeeded);

        var replay = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = firstSession.RefreshToken });

        Assert.False(replay.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, replay.Status);

        var context = harness.CreateUnscopedContext();
        var tokens = await context.RefreshTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == SeedData.Users[0].Id)
            .ToListAsync();

        Assert.Equal(3, tokens.Count); // two sign-ins plus the rotation
        Assert.All(tokens, t => Assert.NotNull(t.RevokedAtUtc));

        // The replacement issued moments ago is dead too, so the thief gains nothing.
        var afterCompromise = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = rotated.Value!.RefreshToken });
        Assert.False(afterCompromise.Succeeded);

        // The other tenant's sessions are untouched by one user's compromise.
        var otherTenant = await SignInAsync(harness, Demo02, "admin@demo02.com");
        var otherRefresh = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = otherTenant.RefreshToken });
        Assert.True(otherRefresh.Succeeded);
    }

    [Fact]
    public async Task Refresh_rejects_an_unknown_token()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var result = await harness.At(Demo01).CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = harness.TokenService.CreateRefreshToken() });

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task Refresh_rejects_an_empty_token()
    {
        using var harness = await AuthTestHarness.CreateAsync();

        var result = await harness.At(Demo01).CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = "   " });

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task Refresh_rejects_an_expired_token()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var login = await SignInAsync(harness, Demo01, "admin@demo01.com");

        var arrange = harness.CreateUnscopedContext();
        var stored = await FindTokenAsync(arrange, harness, login.RefreshToken);
        stored.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await arrange.SaveChangesAsync();

        var result = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    /// <summary>
    /// The check that used to be unreachable. Refresh is anonymous, so nothing else compares the token to
    /// the request: with the address deciding which database the hash is looked up in, a token carried to
    /// another organization's address is refused outright rather than being rotated there.
    /// </summary>
    [Fact]
    public async Task Refresh_rejects_a_token_presented_at_another_organizations_address()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var demo01Session = await SignInAsync(harness, Demo01, "admin@demo01.com");

        var result = await harness.At(Demo02).CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = demo01Session.RefreshToken });

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);

        // Rejected, not consumed: the same token still works at the address it belongs to.
        var owner = await harness.At(Demo01).CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = demo01Session.RefreshToken });
        Assert.True(owner.Succeeded);
    }

    /// <summary>
    /// The second, independent guard: a caller who presents a bearer token as well must have one for the
    /// same organization as the refresh token. The address is held at the token's own organization here, so
    /// the check above cannot be what refuses it — otherwise this test would pass with that guard deleted.
    /// </summary>
    [Fact]
    public async Task Refresh_rejects_a_token_belonging_to_a_different_tenant_than_the_bearer()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var demo01Session = await SignInAsync(harness, Demo01, "admin@demo01.com");

        // At DEMO01's own address, but authenticated as DEMO02.
        harness.TenantContext.TenantId = SeedData.TenantIds.Demo02;
        harness.TenantContext.UserId = SeedData.Users[2].Id;

        var result = await harness.At(Demo01).CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = demo01Session.RefreshToken });

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);

        // Rejected, not consumed: the rightful owner's session still works.
        harness.TenantContext.TenantId = null;
        harness.TenantContext.UserId = null;
        var owner = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = demo01Session.RefreshToken });
        Assert.True(owner.Succeeded);
    }

    [Fact]
    public async Task Refresh_revokes_the_token_when_the_account_has_been_deactivated()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var login = await SignInAsync(harness, Demo01, "hr@demo01.com");

        var arrange = harness.CreateUnscopedContext();
        var user = await arrange.Users.IgnoreQueryFilters().SingleAsync(u => u.Email == "hr@demo01.com");
        user.IsActive = false;
        await arrange.SaveChangesAsync();

        var result = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        Assert.Equal(ResultStatus.Unauthorized, result.Status);

        var assertContext = harness.CreateUnscopedContext();
        var stored = await FindTokenAsync(assertContext, harness, login.RefreshToken);
        Assert.NotNull(stored.RevokedAtUtc);
    }

    [Fact]
    public async Task Refresh_revokes_the_token_when_the_organization_is_suspended()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var login = await SignInAsync(harness, Demo01, "admin@demo01.com");

        var arrange = harness.CreateUnscopedContext();
        var tenant = await arrange.Tenants.SingleAsync(t => t.TenantCode == "DEMO01");
        tenant.Status = TenantStatus.Suspended;
        await arrange.SaveChangesAsync();

        var result = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        Assert.Equal(ResultStatus.Unauthorized, result.Status);

        var assertContext = harness.CreateUnscopedContext();
        var stored = await FindTokenAsync(assertContext, harness, login.RefreshToken);
        Assert.NotNull(stored.RevokedAtUtc);
    }

    /// <summary>Permission changes take effect on the next refresh without forcing a new sign-in.</summary>
    [Fact]
    public async Task Refresh_reflects_a_role_change_made_since_sign_in()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var login = await SignInAsync(harness, Demo01, "hr@demo01.com");
        Assert.Equal(new[] { RoleNames.HRManager }, login.User.Roles);

        // (UserId, RoleId) is the composite key, so the assignment is replaced rather than edited.
        var arrange = harness.CreateUnscopedContext();
        var assignment = await arrange.UserRoles.IgnoreQueryFilters()
            .SingleAsync(ur => ur.UserId == SeedData.Users[1].Id);
        arrange.UserRoles.Remove(assignment);
        arrange.UserRoles.Add(new UserRole
        {
            UserId = SeedData.Users[1].Id,
            RoleId = SeedData.RoleId(RoleNames.HRAdmin),
            TenantId = SeedData.TenantIds.Demo01
        });
        await arrange.SaveChangesAsync();

        var refreshed = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        Assert.True(refreshed.Succeeded);
        Assert.Equal(new[] { RoleNames.HRAdmin }, refreshed.Value!.User.Roles);
        Assert.Contains(Permissions.Employee.Delete, refreshed.Value.User.Permissions);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_is_idempotent()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var login = await SignInAsync(harness, Demo01, "admin@demo01.com");

        harness.TenantContext.TenantId = SeedData.TenantIds.Demo01;
        harness.TenantContext.UserId = SeedData.Users[0].Id;

        var first = await harness.CreateService()
            .LogoutAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });
        var second = await harness.CreateService()
            .LogoutAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);

        var context = harness.CreateUnscopedContext();
        var stored = await FindTokenAsync(context, harness, login.RefreshToken);
        Assert.NotNull(stored.RevokedAtUtc);
    }

    [Fact]
    public async Task Logout_makes_the_refresh_token_unusable()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var login = await SignInAsync(harness, Demo01, "admin@demo01.com");

        harness.TenantContext.TenantId = SeedData.TenantIds.Demo01;
        harness.TenantContext.UserId = SeedData.Users[0].Id;
        await harness.CreateService().LogoutAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        harness.TenantContext.TenantId = null;
        harness.TenantContext.UserId = null;
        var result = await harness.CreateService()
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken });

        Assert.False(result.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    /// <summary>
    /// Sign-out is authenticated, so the tenant query filter applies: one tenant's administrator cannot
    /// revoke another tenant's session even holding the raw token. In the running app this combination never
    /// reaches the service — a token that disagrees with the address is refused by the default authorization
    /// policy first — so this is the layer underneath that, asserted on its own.
    /// </summary>
    [Fact]
    public async Task Logout_cannot_revoke_another_tenants_session()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var victim = await SignInAsync(harness, Demo01, "admin@demo01.com");

        harness.TenantContext.TenantId = SeedData.TenantIds.Demo02;
        harness.TenantContext.UserId = SeedData.Users[2].Id;

        var result = await harness.CreateService()
            .LogoutAsync(new RefreshTokenRequest { RefreshToken = victim.RefreshToken });

        Assert.True(result.Succeeded); // reveals nothing about whether such a token exists

        var context = harness.CreateUnscopedContext();
        var stored = await FindTokenAsync(context, harness, victim.RefreshToken);
        Assert.Null(stored.RevokedAtUtc);
    }

    /// <summary>A user cannot revoke a colleague's session in their own tenant either.</summary>
    [Fact]
    public async Task Logout_cannot_revoke_another_users_session_in_the_same_tenant()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var colleague = await SignInAsync(harness, Demo01, "hr@demo01.com");

        harness.TenantContext.TenantId = SeedData.TenantIds.Demo01;
        harness.TenantContext.UserId = SeedData.Users[0].Id; // the tenant's admin, not the token's owner

        await harness.CreateService().LogoutAsync(new RefreshTokenRequest { RefreshToken = colleague.RefreshToken });

        var context = harness.CreateUnscopedContext();
        var stored = await FindTokenAsync(context, harness, colleague.RefreshToken);
        Assert.Null(stored.RevokedAtUtc);
    }

    /// <summary>
    /// Consuming a refresh token is a conditional UPDATE, so the decision to rotate is made by the
    /// database row and not by the copy the request read moments earlier. Two callers presenting the same
    /// token concurrently therefore cannot both be issued a family — which matters because a stolen token
    /// used alongside the victim's would otherwise rotate cleanly and never be noticed.
    /// <para>
    /// Two services share one DbContext here so the second lookup is answered from the change tracker and
    /// still believes the token is live. That is the state a concurrent request is in, and it reaches the
    /// branch where the update matches no rows. Drop the <c>RevokedAtUtc == null</c> condition from that
    /// update and this test fails with two live families.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Refresh_cannot_consume_the_same_token_twice_when_two_callers_race()
    {
        using var harness = await AuthTestHarness.CreateAsync();
        var session = await SignInAsync(harness, Demo01, "admin@demo01.com");
        var other = await SignInAsync(harness, Demo01, "admin@demo01.com");

        var shared = harness.CreateUnscopedContext();
        var winner = await harness.CreateService(shared)
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = session.RefreshToken });
        var loser = await harness.CreateService(shared)
            .RefreshAsync(new RefreshTokenRequest { RefreshToken = session.RefreshToken });

        Assert.True(winner.Succeeded);
        Assert.False(loser.Succeeded);
        Assert.Equal(ResultStatus.Unauthorized, loser.Status);

        // The loser is treated as a replay, so the user's other live sessions are dropped too.
        var context = harness.CreateUnscopedContext();
        Assert.NotNull((await FindTokenAsync(context, harness, other.RefreshToken)).RevokedAtUtc);
        Assert.NotNull((await FindTokenAsync(context, harness, winner.Value!.RefreshToken)).RevokedAtUtc);
    }

    private static async Task<LoginResponse> SignInAsync(AuthTestHarness harness, string host, string email)
    {
        var result = await harness.At(host).CreateService().LoginAsync(new LoginRequest
        {
            Email = email,
            Password = Password
        });

        Assert.True(result.Succeeded, $"Arrangement failed: could not sign in as {email}.");
        return result.Value!;
    }

    private static async Task<RefreshToken> FindTokenAsync(
        HrmsDbContext context, AuthTestHarness harness, string rawToken)
    {
        var hash = harness.TokenService.HashRefreshToken(rawToken);
        return await context.RefreshTokens.IgnoreQueryFilters().SingleAsync(t => t.TokenHash == hash);
    }
}
