using HRMS.API.Common;
using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;

namespace HRMS.API.Middleware;

/// <summary>
/// Resolves the request's host against the catalog and records the organization it belongs to, so that
/// everything downstream — the rate limiter's partition, authorization, and the tenant <c>DbContext</c>'s
/// connection — is working on one organization's data.
/// <para>
/// This runs before authentication on purpose. The host decides which <em>database</em> is opened; the
/// verified token decides which <em>rows</em> are visible. The host arrives first because the connection has
/// to be chosen before anything can be read, and it is only ever allowed to select a connection — a request
/// still cannot see a single row without a token that agrees with it.
/// </para>
/// </summary>
public sealed class TenantShardResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantShardResolutionMiddleware> _logger;

    public TenantShardResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantShardResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// The resolver and the shard context are taken per invocation rather than through the constructor:
    /// middleware is instantiated once for the lifetime of the app, and both of these are scoped.
    /// </summary>
    public async Task InvokeAsync(
        HttpContext context,
        ITenantShardResolver resolver,
        IShardContext shardContext)
    {
        // Host excludes the port, which Port carries separately — so "demo01.localhost:5173" and
        // "demo01.localhost" resolve to the same organization without any string handling here. Behind a
        // reverse proxy this is whatever the proxy forwarded, which is why forwarded headers have to be
        // configured for a whitelabel deployment to route at all.
        var host = context.Request.Host.Host;
        var shard = await resolver.ResolveByHostAsync(host, context.RequestAborted);

        if (shard is null)
        {
            // No organization signs in at this host: the apex marketing/workspace-picker host, the health
            // probe, or simply a host nobody has bought. Not rejected here — rejecting would take down
            // /health and the apex host with it — because nothing that reads tenant data can proceed anyway.
            // An authenticated request has no shard for its token to agree with, and opening a tenant
            // database without a resolved organization is refused rather than defaulted.
            _logger.LogDebug("No organization is registered for host {Host}; continuing with no shard.", host);
            await _next(context);
            return;
        }

        if (shard.Status != TenantStatus.Active)
        {
            // A known organization that is switched off. Flatly refused, at the edge, before its data is
            // reachable by any route. Distinguishable from an unknown host, which is acceptable: DNS already
            // has to resolve for a real workspace host, so existence is not what is being protected here.
            _logger.LogWarning(
                "Refusing a request for organization {TenantCode} at host {Host}: status is {Status}.",
                shard.TenantCode,
                host,
                shard.Status);

            await WriteWorkspaceUnavailableAsync(context);
            return;
        }

        shardContext.Use(shard);

        _logger.LogDebug(
            "Host {Host} resolved to organization {TenantCode} on shard {ShardKey}.",
            host,
            shard.TenantCode,
            shard.ShardKey);

        await _next(context);
    }

    /// <summary>
    /// One neutral body, in the standard envelope. It names no organization and says nothing about why, so
    /// the response is the same whether an account was suspended for non-payment or closed years ago.
    /// </summary>
    private static Task WriteWorkspaceUnavailableAsync(HttpContext context) =>
        FailureResponse.WriteAsync(
            context,
            StatusCodes.Status404NotFound,
            "This workspace is not available.");
}

/// <summary>Pipeline registration for <see cref="TenantShardResolutionMiddleware"/>.</summary>
public static class TenantShardResolutionMiddlewareExtensions
{
    /// <summary>
    /// Adds host-to-organization resolution. Must sit after <c>UseCors</c> (a preflight carries no session
    /// and is answered before it) and before <c>UseRateLimiter</c>, so the credential limiter can partition
    /// per organization instead of per IP alone.
    /// </summary>
    public static IApplicationBuilder UseTenantShardResolution(this IApplicationBuilder app) =>
        app.UseMiddleware<TenantShardResolutionMiddleware>();
}
