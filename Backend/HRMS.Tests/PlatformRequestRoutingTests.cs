using HRMS.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace HRMS.Tests;

public sealed class PlatformRequestRoutingTests
{
    [Fact]
    public async Task Only_the_exact_local_development_diagnostic_gets_the_bypass_marker()
    {
        var called = false;
        var middleware = new PlatformRequestRoutingMiddleware(_ => { called = true; return Task.CompletedTask; }, Config(), Environment("Development"));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("platform.localhost");
        context.Request.Path = PlatformRequestRoutingMiddleware.CatalogDiagnosticPath;
        context.Request.Method = HttpMethods.Get;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        await middleware.InvokeAsync(context);
        Assert.True(called);
        Assert.True(context.Items.ContainsKey(PlatformRequestRoutingMiddleware.CatalogDiagnosticBypassItem));
    }

    [Fact]
    public async Task Customer_host_cannot_reach_platform_route()
    {
        var called = false;
        var middleware = new PlatformRequestRoutingMiddleware(_ => { called = true; return Task.CompletedTask; }, Config(), Environment("Development"));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("demo01.localhost");
        context.Request.Path = "/api/platform/tenants";
        await middleware.InvokeAsync(context);
        Assert.False(called);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("platform.localhost.evil.test")]
    [InlineData("evil-platform.localhost")]
    [InlineData("unknown.localhost")]
    public async Task Lookalike_or_unknown_hosts_cannot_reach_platform_route(string host)
    {
        var called = false;
        var middleware = new PlatformRequestRoutingMiddleware(_ => { called = true; return Task.CompletedTask; }, Config(), Environment("Development"));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);
        context.Request.Path = "/api/platform/tenants";

        await middleware.InvokeAsync(context);

        Assert.False(called);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task Platform_host_does_not_resolve_a_tenant_for_platform_route()
    {
        var middleware = new PlatformRequestRoutingMiddleware(_ => Task.CompletedTask, Config(), Environment("Development"));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("platform.localhost");
        context.Request.Path = "/api/platform/tenants";

        await middleware.InvokeAsync(context);

        Assert.False(context.Items.ContainsKey(PlatformRequestRoutingMiddleware.CatalogDiagnosticBypassItem));
    }

    [Theory]
    [InlineData("/api/platform/catalog-diagnostic/")]
    [InlineData("/api/platform/catalog-diagnostic/alternate")]
    [InlineData("/api/platform/tenants")]
    public async Task Alternate_platform_routes_do_not_get_the_diagnostic_bypass(string path)
    {
        var middleware = new PlatformRequestRoutingMiddleware(_ => Task.CompletedTask, Config(), Environment("Development"));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("platform.localhost");
        context.Request.Path = path;
        context.Request.Method = HttpMethods.Get;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        await middleware.InvokeAsync(context);

        Assert.False(context.Items.ContainsKey(PlatformRequestRoutingMiddleware.CatalogDiagnosticBypassItem));
    }

    [Fact]
    public async Task Tenant_host_and_forwarded_host_headers_cannot_get_the_diagnostic_bypass()
    {
        var middleware = new PlatformRequestRoutingMiddleware(_ => Task.CompletedTask, Config(), Environment("Development"));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("demo01.localhost");
        context.Request.Headers["X-Forwarded-Host"] = "platform.localhost";
        context.Request.Path = PlatformRequestRoutingMiddleware.CatalogDiagnosticPath;
        context.Request.Method = HttpMethods.Get;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        await middleware.InvokeAsync(context);

        Assert.False(context.Items.ContainsKey(PlatformRequestRoutingMiddleware.CatalogDiagnosticBypassItem));
    }

    [Fact]
    public async Task Production_rejects_the_diagnostic_bypass_even_on_the_allowed_host()
    {
        var middleware = new PlatformRequestRoutingMiddleware(_ => Task.CompletedTask, Config(), Environment("Production"));
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("platform.localhost");
        context.Request.Path = PlatformRequestRoutingMiddleware.CatalogDiagnosticPath;
        context.Request.Method = HttpMethods.Get;
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;

        await middleware.InvokeAsync(context);

        Assert.False(context.Items.ContainsKey(PlatformRequestRoutingMiddleware.CatalogDiagnosticBypassItem));
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:AllowedHosts:0"] = "platform.localhost" })
        .Build();

    private static IHostEnvironment Environment(string name) => new TestEnvironment(name);

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "HRMS.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
