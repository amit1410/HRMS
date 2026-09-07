using HRMS.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace HRMS.Tests;

public sealed class PlatformRequestRoutingTests
{
    [Fact]
    public async Task Platform_host_marks_platform_request_without_resolving_tenant()
    {
        var called = false;
        var middleware = new PlatformRequestRoutingMiddleware(_ => { called = true; return Task.CompletedTask; }, Config());
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("platform.localhost");
        context.Request.Path = "/api/platform/tenants";
        await middleware.InvokeAsync(context);
        Assert.True(called);
        Assert.True(context.Items.ContainsKey(PlatformRequestRoutingMiddleware.PlatformHostItem));
    }

    [Fact]
    public async Task Customer_host_cannot_reach_platform_route()
    {
        var called = false;
        var middleware = new PlatformRequestRoutingMiddleware(_ => { called = true; return Task.CompletedTask; }, Config());
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
        var middleware = new PlatformRequestRoutingMiddleware(_ => { called = true; return Task.CompletedTask; }, Config());
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
        var middleware = new PlatformRequestRoutingMiddleware(_ => Task.CompletedTask, Config());
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("platform.localhost");
        context.Request.Path = "/api/platform/tenants";

        await middleware.InvokeAsync(context);

        Assert.True(context.Items.ContainsKey(PlatformRequestRoutingMiddleware.PlatformHostItem));
    }

    private static IConfiguration Config() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Platform:AllowedHosts:0"] = "platform.localhost" })
        .Build();
}
