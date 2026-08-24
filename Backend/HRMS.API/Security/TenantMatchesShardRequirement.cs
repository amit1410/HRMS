using System.Security.Claims;
using HRMS.Application.Abstractions;
using HRMS.Application.Security;
using Microsoft.AspNetCore.Authorization;

namespace HRMS.API.Security;

/// <summary>
/// The token presented must belong to the organization the request's host resolved to.
/// <para>
/// Two independent things decide what a request may touch: the host selects which <em>database</em> is
/// opened, and the verified token selects which <em>rows</em> are visible within it. Each is safe on its
/// own. Together, if they disagree, both isolation mechanisms are wrong at the same time —
/// <c>ApplyAuditAndTenantStamps</c> writes one organization's <c>TenantId</c> into another's database, and
/// the global query filters interrogate the second organization's tables for the first one's id. The result
/// is not an error but silent empty result sets and mis-stamped rows, which reads as data loss rather than
/// as a rejected request.
/// </para>
/// <para>
/// So the disagreement is refused explicitly here rather than left as an emergent property of whichever
/// mechanism happens to notice first.
/// </para>
/// </summary>
public sealed class TenantMatchesShardRequirement : IAuthorizationRequirement;

/// <inheritdoc cref="TenantMatchesShardRequirement"/>
public sealed class TenantMatchesShardHandler : AuthorizationHandler<TenantMatchesShardRequirement>
{
    private readonly IShardContext _shardContext;
    private readonly ILogger<TenantMatchesShardHandler> _logger;

    public TenantMatchesShardHandler(IShardContext shardContext, ILogger<TenantMatchesShardHandler> logger)
    {
        _shardContext = shardContext;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantMatchesShardRequirement requirement)
    {
        var shard = _shardContext.Current;

        if (shard is null)
        {
            // No organization was resolved for this host, so there is nothing for the token to disagree
            // with. Safe in both deployment modes and load-bearing in neither: with a connection-string
            // template configured, a shard-less scope cannot open a tenant database at all; without one,
            // every organization shares a database and the six query filters isolate them by the same
            // claim this requirement would have compared against.
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.User.Identity is not { IsAuthenticated: true })
        {
            // Anonymous requests are refused by RequireAuthenticatedUser, which every policy carrying this
            // requirement also carries. Succeeding here keeps that the one place a missing identity is
            // reported, rather than turning a sign-in prompt into a workspace-mismatch message.
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Presence of 'tid' is already required by the policy, but presence is not enough: a claim that is
        // present and unparseable leaves ITenantContext with no tenant, which the query filters read as
        // "match nothing" rather than as a bad request. With a shard resolved, that is a disagreement.
        var claimedTenantId = context.User.FindFirstValue(HrmsClaimTypes.TenantId);

        if (!Guid.TryParse(claimedTenantId, out var tenantId) || tenantId != shard.TenantId)
        {
            Refuse(context, requirement, shard, "the token's organization is not the one this host serves");
            return Task.CompletedTask;
        }

        // A free second check with no round trip: 'tcode' is issued alongside 'tid' from the same catalog
        // row, so the two cannot legitimately disagree with the host at once. A renamed organization
        // self-heals — the refresh endpoint is anonymous, so the next refresh issues a token carrying the
        // new code.
        var claimedTenantCode = context.User.FindFirstValue(HrmsClaimTypes.TenantCode);

        if (claimedTenantCode is not null
            && !string.Equals(claimedTenantCode, shard.TenantCode, StringComparison.OrdinalIgnoreCase))
        {
            Refuse(context, requirement, shard, "the token's organization code is not this host's");
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Fails with a reason carrying this handler, which is how
    /// <see cref="ShardMismatchAuthorizationResultHandler"/> tells this refusal apart from a missing
    /// permission and answers 401 instead of 403. The detail is logged and never returned.
    /// </summary>
    private void Refuse(
        AuthorizationHandlerContext context,
        TenantMatchesShardRequirement requirement,
        ShardDescriptor shard,
        string reason)
    {
        _logger.LogWarning(
            "Refusing a request at host {Host} (organization {TenantCode}): {Reason}.",
            shard.Host,
            shard.TenantCode,
            reason);

        context.Fail(new AuthorizationFailureReason(this, reason));
    }
}
