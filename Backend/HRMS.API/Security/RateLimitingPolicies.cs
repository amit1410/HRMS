using System.Threading.RateLimiting;
using HRMS.API.Common;
using HRMS.Application.Abstractions;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace HRMS.API.Security;

/// <summary>Named rate-limiting policies referenced by <c>[EnableRateLimiting]</c>.</summary>
public static class RateLimitingPolicies
{
    /// <summary>Throttles credential-handling endpoints (sign-in, refresh).</summary>
    public const string Authentication = "auth";
}

/// <summary>
/// Registers request rate limiting. Sign-in and refresh are throttled per organization and client IP to
/// blunt password guessing and credential stuffing, which no amount of password hashing prevents on its own.
/// </summary>
public static class RateLimitingServiceCollectionExtensions
{
    public static IServiceCollection AddHrmsRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthenticationRateLimitSettings>()
            .Bind(configuration.GetSection(AuthenticationRateLimitSettings.SectionName))
            .Validate(settings => settings.Validate() is null,
                $"The '{AuthenticationRateLimitSettings.SectionName}' configuration section is invalid.")
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(RateLimitingPolicies.Authentication, httpContext =>
            {
                // Read through IOptions rather than capturing values from the configuration passed at
                // registration: the limit is then whatever the assembled configuration actually says.
                var settings = httpContext.RequestServices
                    .GetRequiredService<IOptions<AuthenticationRateLimitSettings>>().Value;

                // Partitioned by (organization, remote IP). The IP alone is not enough once every
                // organization signs in at its own host: a whole office behind one NAT address, or a
                // corporate egress gateway, shares a single partition, and one organization's sign-ins then
                // throttle another's. The host-resolution middleware runs before the limiter, so the
                // organization is already known here; requests to an unresolved host fall back to IP alone.
                var tenant = httpContext.RequestServices.GetRequiredService<IShardContext>().Current;
                var address = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    // The real client address, not the proxy's, because UseForwardedHeaders runs first and
                    // honours X-Forwarded-For from the proxies listed under "ForwardedHeaders". Without that
                    // configuration every request behind a load balancer shares one partition.
                    partitionKey: tenant is null ? address : $"{tenant.TenantId:N}|{address}",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                    });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                // Guarded before the header write, not only inside the writer: setting a header on a
                // response that has already started throws.
                if (context.HttpContext.Response.HasStarted)
                {
                    return;
                }

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                await FailureResponse.WriteAsync(
                    context.HttpContext,
                    StatusCodes.Status429TooManyRequests,
                    "Too many attempts. Please wait a moment and try again.",
                    cancellationToken);
            };
        });

        return services;
    }
}
