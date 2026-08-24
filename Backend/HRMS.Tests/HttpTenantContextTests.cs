using System.Security.Claims;
using HRMS.API.Security;
using HRMS.Application.Security;
using Microsoft.AspNetCore.Http;

namespace HRMS.Tests;

/// <summary>
/// Tenant identity must come only from a verified token. These tests pin the two ways that can go wrong:
/// claims on an unauthenticated principal, and claims that are present but unusable.
/// </summary>
public class HttpTenantContextTests
{
    private static readonly Guid TenantId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = new("a1111111-1111-1111-1111-111111111111");

    [Fact]
    public void Resolves_tenant_and_user_from_an_authenticated_principal()
    {
        var context = CreateContext(Principal("Bearer", new Claim(HrmsClaimTypes.TenantId, TenantId.ToString()),
            new Claim(HrmsClaimTypes.UserId, UserId.ToString())));

        Assert.Equal(TenantId, context.TenantId);
        Assert.Equal(UserId, context.UserId);
        Assert.True(context.HasTenant);
    }

    /// <summary>
    /// The critical case: a principal carrying tenant claims that was never authenticated (a rejected
    /// token, or claims injected earlier in the pipeline) must resolve to no tenant at all.
    /// </summary>
    [Fact]
    public void Ignores_claims_on_an_unauthenticated_principal()
    {
        // authenticationType null => ClaimsIdentity.IsAuthenticated is false, even with claims present.
        var context = CreateContext(Principal(null, new Claim(HrmsClaimTypes.TenantId, TenantId.ToString()),
            new Claim(HrmsClaimTypes.UserId, UserId.ToString())));

        Assert.Null(context.TenantId);
        Assert.Null(context.UserId);
        Assert.False(context.HasTenant);
    }

    [Fact]
    public void Resolves_nothing_when_there_is_no_request()
    {
        var context = new HttpTenantContext(new HttpContextAccessor());

        Assert.Null(context.TenantId);
        Assert.Null(context.UserId);
        Assert.False(context.HasTenant);
    }

    [Fact]
    public void Resolves_nothing_when_the_authenticated_principal_has_no_tenant_claim()
    {
        var context = CreateContext(Principal("Bearer", new Claim(HrmsClaimTypes.Email, "someone@demo01.com")));

        Assert.Null(context.TenantId);
        Assert.Null(context.UserId);
        Assert.False(context.HasTenant);
    }

    [Fact]
    public void Rejects_a_tenant_claim_that_is_not_a_guid()
    {
        var context = CreateContext(Principal("Bearer",
            new Claim(HrmsClaimTypes.TenantId, "'; DROP TABLE Users; --"),
            new Claim(HrmsClaimTypes.UserId, "not-a-guid")));

        Assert.Null(context.TenantId);
        Assert.Null(context.UserId);
    }

    private static HttpTenantContext CreateContext(ClaimsPrincipal principal)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
        return new HttpTenantContext(accessor);
    }

    private static ClaimsPrincipal Principal(string? authenticationType, params Claim[] claims) =>
        new(new ClaimsIdentity(claims, authenticationType));
}
