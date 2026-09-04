using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace HRMS.Tests;

/// <summary>
/// End-to-end tests over the real HTTP pipeline: host resolution, the JWT bearer handler, authorization
/// policies, the validation filter, the response envelope and the tenant context all participate. These
/// cover what unit tests on <c>AuthService</c> cannot — that the tenant a request operates on comes from a
/// verified token, at an address that agrees with it.
/// <para>
/// Anything that signs in uses <see cref="HrmsApiFactory.CreateClientFor"/>: the organization is the
/// address, so a client on the default <c>localhost</c> has no organization to sign in to. Tests that only
/// exercise token validation stay on the default client, because they never reach tenant data.
/// </para>
/// </summary>
public class AuthEndpointsTests : IClassFixture<HrmsApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly Guid Demo01AdminId = SeedData.Users[0].Id;
    private static readonly Guid Demo01HrId = SeedData.Users[1].Id;

    private readonly HrmsApiFactory _factory;

    public AuthEndpointsTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_returns_a_token_pair_and_the_users_profile()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@demo01.com",
            Password = SeedData.DefaultUserPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadAsync<LoginResponse>(response);
        Assert.True(body.Success);
        Assert.NotNull(body.Data);
        Assert.False(string.IsNullOrWhiteSpace(body.Data!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(body.Data.RefreshToken));

        // Nothing in the request said DEMO01. The address did.
        Assert.Equal("DEMO01", body.Data.User.TenantCode);
        Assert.Contains(RoleNames.TenantAdmin, body.Data.User.Roles);
        Assert.Equal(SeedData.RolePermissionMap[RoleNames.TenantAdmin].Length, body.Data.User.Permissions.Count);
    }

    /// <summary>Nothing derived from the stored credential may appear in a response.</summary>
    [Fact]
    public async Task Login_response_contains_no_password_material()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@demo01.com",
            Password = SeedData.DefaultUserPassword
        });

        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("password", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SeedData.DefaultUserPassword, raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_with_bad_credentials_returns_401_in_the_standard_envelope()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@demo01.com",
            Password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await ReadAsync<LoginResponse>(response);
        Assert.False(body.Success);
        Assert.Null(body.Data);
        Assert.False(string.IsNullOrWhiteSpace(body.Message));
    }

    /// <summary>
    /// The validation filter still reports the fields the request does have. It must not report one it does
    /// not: a leftover rule on the removed organization code would 400 every sign-in with a message naming a
    /// field no form can fill in, which is why the absence is asserted rather than assumed.
    /// </summary>
    [Fact]
    public async Task Login_with_a_malformed_email_and_no_password_returns_400_with_field_errors()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "not-an-email",
            Password = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadAsync<LoginResponse>(response);
        Assert.False(body.Success);
        Assert.NotNull(body.Errors);
        var fields = body.Errors!.Select(e => e.Field).ToList();
        Assert.Contains("email", fields);
        Assert.Contains("password", fields);
        Assert.DoesNotContain("tenantCode", fields);
    }

    /// <summary>
    /// What used to be "a missing organization code is rejected". Sending correct credentials to an address
    /// no organization uses is refused before any credential is checked, and refused distinguishably — the
    /// address is the problem, and reporting it as a bad password would send the user to fix the wrong thing.
    /// </summary>
    [Fact]
    public async Task Login_at_an_address_no_organization_uses_is_refused()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.UnknownHost);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "admin@demo01.com",
            Password = SeedData.DefaultUserPassword
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await ReadAsync<LoginResponse>(response);
        Assert.False(body.Success);
        Assert.Null(body.Data);
        Assert.Contains("no organization at this address", body.Message, StringComparison.OrdinalIgnoreCase);

        // It names no organization, so it cannot be read as a list of who our customers are.
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("DEMO01", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Me_requires_a_bearer_token()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await ReadAsync<AuthenticatedUserDto>(response);
        Assert.False(body.Success);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user_for_a_token_the_api_issued()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        var session = await SignInAsync(client, "hr@demo01.com");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ReadAsync<AuthenticatedUserDto>(response);
        Assert.Equal(Demo01HrId, body.Data!.Id);
        Assert.Equal(SeedData.TenantIds.Demo01, body.Data.TenantId);
        Assert.Equal(new[] { RoleNames.HRManager }, body.Data.Roles);
    }

    /// <summary>
    /// The decisive multi-tenant test at the HTTP boundary. The token is validly signed and would pass every
    /// signature check, but names tenant DEMO02 while identifying a DEMO01 user. The tenant claim is what
    /// reaches the database, so the user is invisible and the request cannot read across the boundary.
    /// <para>
    /// Presented at DEMO02's own address deliberately: the token and the host agree, so the requirement that
    /// compares them passes and the query filter is the only thing left to refuse it. Sent to a host that
    /// resolves to nothing, this would pass even if the filter did nothing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_token_naming_another_tenant_cannot_reach_this_tenants_data()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo02Host);

        var forged = TestTokens.Create(
            userId: Demo01AdminId,
            tenantId: SeedData.TenantIds.Demo02,
            tenantCode: "DEMO02",
            roles: [RoleNames.TenantAdmin],
            permissions: Permissions.All);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await ReadAsync<AuthenticatedUserDto>(response);
        Assert.False(body.Success);
        Assert.Null(body.Data);
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        using var client = _factory.CreateClient();

        var token = TestTokens.Create(
            Demo01AdminId,
            SeedData.TenantIds.Demo01,
            signingKey: "an-attackers-signing-key-of-entirely-sufficient-length-for-hmac");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Algorithm confusion: the token is signed with the correct secret but a different HMAC algorithm.
    /// The handler pins the accepted algorithm, so it is refused.
    /// </summary>
    [Fact]
    public async Task A_token_signed_with_a_different_algorithm_is_refused()
    {
        using var client = _factory.CreateClient();

        var token = TestTokens.Create(
            Demo01AdminId,
            SeedData.TenantIds.Demo01,
            algorithm: SecurityAlgorithms.HmacSha512);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task An_expired_token_is_refused()
    {
        using var client = _factory.CreateClient();

        var token = TestTokens.Create(
            Demo01AdminId,
            SeedData.TenantIds.Demo01,
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_issued_for_another_audience_is_refused()
    {
        using var client = _factory.CreateClient();

        var token = TestTokens.Create(Demo01AdminId, SeedData.TenantIds.Demo01, audience: "Some.Other.Client");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// A validly signed token without a tenant claim identifies no workspace session, so it is challenged
    /// rather than treated as an authenticated user who merely lacks a permission.
    /// </summary>
    [Fact]
    public async Task A_token_without_a_tenant_claim_is_refused()
    {
        // Use a resolved workspace so this test isolates the missing claim rather than also triggering the
        // unresolved-host guard.
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);

        var token = TestTokens.Create(Demo01AdminId, SeedData.TenantIds.Demo01, includeTenantClaim: false);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_the_pair_and_the_old_token_stops_working()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        var session = await SignInAsync(client, "admin@demo01.com");

        var refreshResponse = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = session.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshed = (await ReadAsync<LoginResponse>(refreshResponse)).Data!;
        Assert.NotEqual(session.RefreshToken, refreshed.RefreshToken);

        // The new access token works.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        // Replaying the consumed refresh token fails.
        var replay = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    /// <summary>
    /// A refresh token is only good at the address it was issued at. Over HTTP that is the whole of the
    /// check: the request body is identical in both calls below, so the address is the only difference.
    /// </summary>
    [Fact]
    public async Task Refresh_is_refused_at_another_organizations_address()
    {
        using var demo01 = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        using var demo02 = _factory.CreateClientFor(HrmsApiFactory.Demo02Host);

        var session = await SignInAsync(demo01, "admin@demo01.com");
        var body = new RefreshTokenRequest { RefreshToken = session.RefreshToken };

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await demo02.PostAsJsonAsync("/api/auth/refresh", body)).StatusCode);

        // Refused, not consumed: it still rotates at its own address.
        Assert.Equal(
            HttpStatusCode.OK,
            (await demo01.PostAsJsonAsync("/api/auth/refresh", body)).StatusCode);
    }

    [Fact]
    public async Task An_access_token_cannot_be_used_as_a_refresh_token()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        var session = await SignInAsync(client, "admin@demo01.com");

        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = session.AccessToken });

        // Refused on shape (a JWT far exceeds the refresh-token length cap) before any lookup happens.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_with_an_empty_token_is_rejected_by_validation()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);

        var response = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Logout_requires_authentication_and_then_invalidates_the_session()
    {
        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        var session = await SignInAsync(client, "hr@demo01.com");

        // Anonymous sign-out is refused, so a stolen refresh token alone cannot be used to disrupt a user.
        var anonymous = await client.PostAsJsonAsync("/api/auth/logout",
            new RefreshTokenRequest { RefreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var logout = await client.PostAsJsonAsync("/api/auth/logout",
            new RefreshTokenRequest { RefreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh",
            new RefreshTokenRequest { RefreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    /// <summary>
    /// Two organizations, two addresses, one running API. Each client is fixed to its own host for the whole
    /// exchange, which is what a real pair of browsers would be.
    /// </summary>
    [Fact]
    public async Task Both_tenants_can_sign_in_concurrently_and_each_sees_only_itself()
    {
        using var demo01Client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        using var demo02Client = _factory.CreateClientFor(HrmsApiFactory.Demo02Host);

        var demo01 = await SignInAsync(demo01Client, "admin@demo01.com");
        var demo02 = await SignInAsync(demo02Client, "admin@demo02.com");

        demo01Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", demo01.AccessToken);
        var first = (await ReadAsync<AuthenticatedUserDto>(await demo01Client.GetAsync("/api/auth/me"))).Data!;

        demo02Client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", demo02.AccessToken);
        var second = (await ReadAsync<AuthenticatedUserDto>(await demo02Client.GetAsync("/api/auth/me"))).Data!;

        Assert.Equal(SeedData.TenantIds.Demo01, first.TenantId);
        Assert.Equal(SeedData.TenantIds.Demo02, second.TenantId);
        Assert.Equal("Demo Organization", first.TenantName);
        Assert.Equal("Sample Organization", second.TenantName);
    }

    /// <summary>
    /// One authorization policy must exist per permission name, and the default policy must require a
    /// tenant claim — otherwise a tenant-less token could satisfy a bare <c>[Authorize]</c>.
    /// </summary>
    [Fact]
    public async Task Every_permission_has_a_registered_authorization_policy()
    {
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var permission in Permissions.All)
        {
            var policy = await provider.GetPolicyAsync(permission);
            Assert.NotNull(policy);
        }

        var defaultPolicy = await provider.GetDefaultPolicyAsync();
        Assert.Contains(defaultPolicy.Requirements, r => r is ClaimsAuthorizationRequirement
        {
            ClaimType: "tid"
        });
    }

    /// <summary>
    /// An endpoint that declares no authorization at all must still be closed: the fallback policy carries
    /// the same requirements as the default one, so a controller added later that forgets
    /// <c>[Authorize]</c> is refused rather than silently public. The endpoints that are meant to be open
    /// say so with <c>[AllowAnonymous]</c>, and this checks that closing everything did not close them.
    /// </summary>
    [Fact]
    public async Task An_endpoint_that_declares_no_authorization_is_closed_by_default()
    {
        var provider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var fallback = await provider.GetFallbackPolicyAsync();
        Assert.NotNull(fallback);
        Assert.Contains(fallback!.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        Assert.Contains(fallback.Requirements, r => r is ClaimsAuthorizationRequirement { ClaimType: "tid" });

        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/system/info")).StatusCode);
    }

    /// <summary>
    /// Credential endpoints are throttled per client, so password guessing is limited regardless of how
    /// fast the hash verifies. This factory is created with a deliberately tiny limit.
    /// </summary>
    [Fact]
    public async Task Repeated_sign_in_attempts_are_throttled()
    {
        using var throttled = HrmsApiFactory.WithAuthPermitLimit(3);
        using var client = throttled.CreateClientFor(HrmsApiFactory.Demo01Host);

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
            {
                Email = "admin@demo01.com",
                Password = "guess"
            });
            statuses.Add(response.StatusCode);
        }

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(2, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    /// <summary>
    /// Signs in on the supplied client, which is already addressed to one organization — there is nothing
    /// left to pass but the email.
    /// </summary>
    private static async Task<LoginResponse> SignInAsync(HttpClient client, string email)
    {
        var previousAuthorization = client.DefaultRequestHeaders.Authorization;
        client.DefaultRequestHeaders.Authorization = null;

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = SeedData.DefaultUserPassword
        });

        client.DefaultRequestHeaders.Authorization = previousAuthorization;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsync<LoginResponse>(response)).Data!;
    }

    private static async Task<ApiResponse<T>> ReadAsync<T>(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        var body = JsonSerializer.Deserialize<ApiResponse<T>>(payload, Json);
        Assert.NotNull(body);
        return body!;
    }
}
