using HRMS.API.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace HRMS.API.Security;

/// <summary>
/// Answers a host/token disagreement with 401 instead of the 403 that a failed authorization requirement
/// would otherwise produce.
/// <para>
/// The distinction is not cosmetic. 403 means "you are signed in, but you may not do this", and a client
/// that receives it shows a permission error and keeps the session. 401 means "these credentials are not
/// valid here", which is exactly the situation: the token is genuine, it simply belongs to a different
/// workspace than the one being addressed. The client should sign in again at this host, and only 401
/// tells it to.
/// </para>
/// <para>
/// Every other authorization outcome is delegated untouched to the framework's own handler, so permission
/// failures, anonymous challenges and successes all behave exactly as before.
/// </para>
/// </summary>
public sealed class ShardMismatchAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (IsShardMismatch(authorizeResult))
        {
            // Neutral, and deliberately not the same text as the bearer challenge: nothing is wrong with
            // the token itself, so "supply a valid bearer token" would send the client in circles. Names no
            // organization — the reason is logged by the handler that refused, not returned.
            return FailureResponse.WriteAsync(
                context,
                StatusCodes.Status401Unauthorized,
                "Your session is not valid for this workspace. Please sign in again.");
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }

    /// <summary>
    /// Identified by the handler that recorded the failure rather than by the requirement, because an
    /// explicit <c>Fail</c> reports itself through <c>FailureReasons</c> and leaves
    /// <c>FailedRequirements</c> empty.
    /// </summary>
    private static bool IsShardMismatch(PolicyAuthorizationResult authorizeResult) =>
        authorizeResult.AuthorizationFailure?.FailureReasons
            .Any(reason => reason.Handler is TenantMatchesShardHandler) == true;
}
