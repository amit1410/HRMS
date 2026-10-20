using System.Security.Claims;
using HRMS.API.Controllers;
using HRMS.API.Middleware;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Security;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Sharding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.Tests;

public sealed class CatalogDiagnosticSecurityTests
{
    [Fact]
    public void Resolver_returns_only_safe_metadata_and_never_credentials()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:CatalogProvider"] = "MySql",
            ["ConnectionStrings:Catalog"] = "Server=catalog.example;Port=3307;Database=hrms_catalog;User ID=secret-user;Password=secret-password"
        }).Build();

        var details = CatalogConnectionResolver.Resolve(configuration);

        Assert.Equal("MySql", details.Provider);
        Assert.Equal("catalog.example", details.Host);
        Assert.Equal(3307, details.Port);
        Assert.Equal("hrms_catalog", details.Database);
        Assert.DoesNotContain("secret", details.ConfigurationResolution, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Platform_super_admin_policy_is_claim_based_and_does_not_require_a_catalog_permission_handler()
    {
        var services = new ServiceCollection();
        services.AddJwtAuthentication(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "test-signing-key-test-signing-key-test-signing-key-test-signing-key",
            ["Jwt:Issuer"] = "issuer",
            ["Jwt:Audience"] = "audience"
        }).Build());
        using var provider = services.BuildServiceProvider();
        var policy = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var result = policy.GetPolicyAsync("PlatformSuperAdmin").GetAwaiter().GetResult();

        Assert.NotNull(result);
        Assert.Contains(result!.AuthenticationSchemes, scheme => scheme == "PlatformBearer");
        Assert.Contains(result.Requirements, requirement => requirement is ClaimsAuthorizationRequirement claim
            && claim.ClaimType == PlatformClaimTypes.Scope
            && claim.AllowedValues?.Contains("platform") == true);
        Assert.Contains(result.Requirements, requirement => requirement is ClaimsAuthorizationRequirement claim
            && claim.ClaimType == HrmsClaimTypes.Role
            && claim.AllowedValues?.Contains("PlatformSuperAdmin") == true);
        Assert.DoesNotContain(result.Requirements, requirement => requirement is PlatformPermissionRequirement);
    }

    [Fact]
    public async Task Diagnostic_policy_rejects_anonymous_requests()
    {
        var services = new ServiceCollection();
        services.AddJwtAuthentication(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "test-signing-key-test-signing-key-test-signing-key-test-signing-key",
            ["Jwt:Issuer"] = "issuer",
            ["Jwt:Audience"] = "audience",
            ["PlatformJwt:SecretKey"] = "platform-signing-key-platform-signing-key-platform-signing-key",
            ["PlatformJwt:Issuer"] = "platform-issuer",
            ["PlatformJwt:Audience"] = "platform-audience"
        }).Build());
        services.AddSingleton<IShardContext, ShardContext>();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(new ClaimsPrincipal(new ClaimsIdentity()), null, "PlatformSuperAdmin");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Platform_bearer_validation_keeps_expiry_algorithm_and_token_not_saved()
    {
        var services = new ServiceCollection();
        services.AddJwtAuthentication(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "test-signing-key-test-signing-key-test-signing-key-test-signing-key",
            ["Jwt:Issuer"] = "issuer",
            ["Jwt:Audience"] = "audience",
            ["PlatformJwt:SecretKey"] = "platform-signing-key-platform-signing-key-platform-signing-key",
            ["PlatformJwt:Issuer"] = "platform-issuer",
            ["PlatformJwt:Audience"] = "platform-audience"
        }).Build());
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get("PlatformBearer");

        Assert.True(options.TokenValidationParameters.ValidateLifetime);
        Assert.True(options.TokenValidationParameters.ValidateIssuer);
        Assert.True(options.TokenValidationParameters.ValidateAudience);
        Assert.Contains(SecurityAlgorithms.HmacSha256, options.TokenValidationParameters.ValidAlgorithms);
        Assert.False(options.SaveToken);
    }

    [Fact]
    public void Diagnostic_controller_has_no_catalog_or_db_context_dependency_and_rejects_non_development()
    {
        var controller = new CatalogDiagnosticController(
            new ConfigurationBuilder().Build(),
            new TestEnvironment("Production"));

        var result = controller.Get().Result;

        Assert.IsType<NotFoundResult>(result);
        Assert.DoesNotContain(typeof(IHrmsCatalogDbContext), typeof(CatalogDiagnosticController).GetConstructors()[0].GetParameters().Select(parameter => parameter.ParameterType));
        Assert.DoesNotContain(typeof(HRMS.Infrastructure.Persistence.HrmsDbContext), typeof(CatalogDiagnosticController).GetConstructors()[0].GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public async Task Only_the_diagnostic_marker_bypasses_tenant_catalog_resolution()
    {
        var called = false;
        var middleware = new TenantShardResolutionMiddleware(
            _ =>
            {
                called = true;
                return Task.CompletedTask;
            },
            NullLogger<TenantShardResolutionMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = PlatformRequestRoutingMiddleware.CatalogDiagnosticPath;
        context.Items[PlatformRequestRoutingMiddleware.CatalogDiagnosticBypassItem] = true;

        await middleware.InvokeAsync(context, new ThrowingResolver(), new ShardContext());

        Assert.True(called);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class ThrowingResolver : ITenantShardResolver
    {
        public Task<ShardDescriptor?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Tenant catalog resolution must not run for the diagnostic.");
    }
}
