using System.Security.Claims;
using HRMS.API.Controllers;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Security;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

/// <summary>
/// Database-free checks for the authorization boundary around the read-only invitation surface.
/// These tests deliberately invoke policy evaluation only; they do not build an EF context or connect
/// to the catalog or a tenant shard.
/// </summary>
public sealed class WelcomeInvitationAuthorizationTests
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task Anonymous_caller_is_rejected_by_the_view_policy()
    {
        var result = await AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), TenantA);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Authenticated_caller_without_view_permission_is_rejected()
    {
        var user = User(TenantA);

        var result = await AuthorizeAsync(user, TenantA);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Caller_with_view_permission_is_rejected_when_host_resolves_to_another_tenant()
    {
        var user = User(TenantA, Permissions.WelcomeInvitation.View);

        var result = await AuthorizeAsync(user, TenantB);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Tenant_admin_view_permission_is_accepted_when_tenant_matches_host()
    {
        var user = User(TenantA, Permissions.WelcomeInvitation.View, role: RoleNames.TenantAdmin);

        var result = await AuthorizeAsync(user, TenantA);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Every_invitation_endpoint_declares_its_required_permission()
    {
        var methods = typeof(WelcomeInvitationsController).GetMethods()
            .Where(method => method.DeclaringType == typeof(WelcomeInvitationsController));

        Assert.NotEmpty(methods);
        Assert.All(methods, method => Assert.NotNull(method.GetCustomAttributes(typeof(HasPermissionAttribute), inherit: true).SingleOrDefault()));
    }

    private static ClaimsPrincipal User(Guid tenantId, string permission = "unused", string? role = null)
    {
        var claims = new List<Claim>
        {
            new(HrmsClaimTypes.TenantId, tenantId.ToString())
        };

        if (permission != "unused")
            claims.Add(new(HrmsClaimTypes.Permission, permission));
        if (role is not null)
            claims.Add(new(HrmsClaimTypes.Role, role));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static async Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, Guid resolvedTenantId)
    {
        var shard = new TestShardContext(new ShardDescriptor(
            resolvedTenantId,
            "WEB1",
            "web1.example.test",
            "web1",
            TenantStatus.Active,
            DatabaseProviderType.MySql));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IShardContext>(shard);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<TenantMatchesShardHandler>>(
            NullLogger<TenantMatchesShardHandler>.Instance);
        services.AddAuthorization(options => options.AddPolicy(
            Permissions.WelcomeInvitation.View,
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim(HrmsClaimTypes.TenantId)
                .RequireClaim(HrmsClaimTypes.Permission, Permissions.WelcomeInvitation.View)
                .AddRequirements(new TenantMatchesShardRequirement())
                .Build()));
        services.AddSingleton<IAuthorizationHandler, TenantMatchesShardHandler>();

        await using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        return await authorization.AuthorizeAsync(user, null, Permissions.WelcomeInvitation.View);
    }

    private sealed class TestShardContext(ShardDescriptor current) : IShardContext
    {
        public ShardDescriptor? Current { get; } = current;
        public bool HasShard => Current is not null;
        public void Use(ShardDescriptor descriptor) => throw new NotSupportedException();
    }
}
