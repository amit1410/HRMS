using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Auth;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;

namespace HRMS.Tests;

/// <summary>
/// The host picks the database; the token picks the rows. This is what happens when they disagree.
/// <para>
/// Each mechanism is safe alone, which is what makes the combination dangerous: with a genuine token for
/// one organization presented at another organization's host, <c>ApplyAuditAndTenantStamps</c> would stamp
/// the first organization's <c>TenantId</c> onto rows written into the second's database, and the global
/// query filters would interrogate the second organization's tables for the first one's id. Neither
/// mechanism reports an error — the result is mis-stamped rows and empty result sets that read as data loss.
/// </para>
/// </summary>
public class TokenHostAgreementTests : IClassFixture<HrmsApiFactory>
{
    private const string Demo01Host = HrmsApiFactory.Demo01Host;
    private const string Demo02Host = HrmsApiFactory.Demo02Host;

    /// <summary><c>[Authorize]</c>, so this route is governed by the default policy.</summary>
    private const string DefaultPolicyRoute = "/api/auth/me";

    /// <summary>
    /// <c>[HasPermission(Employee.View)]</c>, so this route is governed by a *named* policy. A named policy
    /// replaces the default one rather than adding to it, so this is a genuinely separate code path — and the
    /// one that guards employee data.
    /// </summary>
    private const string PermissionPolicyRoute = "/api/employees";

    private readonly HrmsApiFactory _factory;

    public TokenHostAgreementTests(HrmsApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task A_token_is_refused_at_another_organizations_host()
    {
        var token = await TokenForAsync(Demo01Host, "admin@demo01.com");
        using var client = ClientFor(Demo02Host, token);

        var response = await client.GetAsync(DefaultPolicyRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// The same check on a permission-guarded route. Worth its own test because named policies do not
    /// inherit the default policy's requirements: a version of this change that only added the requirement
    /// to <c>DefaultPolicy</c> would pass the test above and leave every employee-data route unguarded.
    /// </summary>
    [Fact]
    public async Task A_token_is_refused_at_another_organizations_host_on_a_permission_guarded_route()
    {
        var token = await TokenForAsync(Demo01Host, "admin@demo01.com");
        using var client = ClientFor(Demo02Host, token);

        var response = await client.GetAsync(PermissionPolicyRoute);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// 401 rather than 403, and the difference is behavioural: 403 tells a client it is signed in but
    /// unauthorized, so it shows a permission error and keeps the session. The token here is genuine and
    /// simply belongs elsewhere, so the client needs to sign in at this host — which only 401 prompts.
    /// </summary>
    [Fact]
    public async Task The_refusal_tells_the_client_to_sign_in_again_and_names_no_organization()
    {
        var token = await TokenForAsync(Demo01Host, "admin@demo01.com");
        using var client = ClientFor(Demo02Host, token);

        var response = await client.GetAsync(DefaultPolicyRoute);
        var body = await response.Content.ReadFromJsonAsync<ApiResponse>();

        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.Equal("Your session is not valid for this workspace. Please sign in again.", body.Message);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("DEMO01", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DEMO02", raw, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The control. Without this, a requirement that refused everything would satisfy every test above.
    /// </summary>
    [Fact]
    public async Task A_token_works_at_its_own_organizations_host()
    {
        var token = await TokenForAsync(Demo01Host, "admin@demo01.com");
        using var client = ClientFor(Demo01Host, token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(DefaultPolicyRoute)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(PermissionPolicyRoute)).StatusCode);
    }

    /// <summary>
    /// A tenant token is not valid at an address that resolves to no workspace. This matters in shared-
    /// database mode, where the unresolved scope can otherwise open the shared database and select rows by
    /// token claim alone, bypassing the host/workspace half of the isolation boundary.
    /// </summary>
    [Fact]
    public async Task A_token_is_refused_at_a_host_that_resolves_to_no_workspace()
    {
        var token = await TokenForAsync(Demo01Host, "admin@demo01.com");

        // The factory's default base address is plain "localhost", which is not a seeded workspace host.
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(DefaultPolicyRoute)).StatusCode);
    }

    /// <summary>
    /// An ordinary missing permission is still 403. The result handler that produces the 401 above has to
    /// intercept exactly one kind of failure; if it caught authorization failures in general, every
    /// permission error in the app would start telling users their session was invalid.
    /// </summary>
    [Fact]
    public async Task A_missing_permission_at_the_right_host_is_still_forbidden()
    {
        // The HR manager role carries Employee.View but not Employee.Delete.
        var token = await TokenForAsync(Demo01Host, "hr@demo01.com");
        using var client = ClientFor(Demo01Host, token);

        var response = await client.DeleteAsync($"{PermissionPolicyRoute}/{Guid.NewGuid()}");

        // Authorization runs before the handler, so the id needing to exist is not in question.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient ClientFor(string host, string accessToken)
    {
        var client = _factory.CreateClientFor(host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    /// <summary>
    /// Signs in at the organization's own host — which is the only way to sign in, since the host is what
    /// says which organization's credentials are being checked. Sign-in is anonymous, so it is not subject to
    /// the check under test; issuing the token from the host it belongs to is what makes the cross-host
    /// request above a genuine mismatch rather than an artefact of how the token was obtained.
    /// </summary>
    private async Task<string> TokenForAsync(string host, string email)
    {
        using var client = _factory.CreateClientFor(host);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = email,
            Password = SeedData.DefaultUserPassword
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiResponse<LoginResponse>>();
        return body!.Data!.AccessToken;
    }
}
