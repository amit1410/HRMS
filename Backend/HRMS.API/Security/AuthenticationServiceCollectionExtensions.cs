using System.Text;
using HRMS.API.Common;
using HRMS.Application.Security;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HRMS.API.Security;

/// <summary>
/// Wires JWT bearer authentication and the authorization policies that back
/// <see cref="HasPermissionAttribute"/>.
/// </summary>
public static class AuthenticationServiceCollectionExtensions
{
    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        // Bound once and consumed through IOptions everywhere. Reading the section here instead — and
        // capturing the values in the delegates below — would snapshot configuration as it stood mid
        // registration, so the signing key used to *issue* tokens could differ from the one used to
        // *validate* them whenever a later configuration source changes it.
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        // JwtSettings is resolved when the handler's options are first built, which is after the host has
        // finished assembling configuration — the same values JwtTokenService signs with.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, jwtSettings) =>
            {
                var settings = jwtSettings.Value;

                // Keep the compact claim names exactly as issued. With mapping enabled, "email" would be
                // rewritten to a WS-Federation URI and the tenant context would stop finding its claims.
                options.MapInboundClaims = false;
                options.SaveToken = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = settings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = settings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.SecretKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(settings.ClockSkewSeconds),

                    // Pin the algorithm so a token presented with "none" or a swapped algorithm is rejected.
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],

                    NameClaimType = HrmsClaimTypes.Email,
                    RoleClaimType = HrmsClaimTypes.Role
                };

                // Return the standard response envelope for auth failures instead of an empty body.
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await FailureResponse.WriteAsync(
                            context.HttpContext,
                            StatusCodes.Status401Unauthorized,
                            "Authentication is required. Supply a valid bearer token.");
                    },
                    OnForbidden = context => FailureResponse.WriteAsync(
                        context.HttpContext,
                        StatusCodes.Status403Forbidden,
                        "You do not have permission to perform this action.")
                };
            });

        services.AddAuthorization(options =>
        {
            // Every [Authorize] endpoint requires an authenticated user, a tenant claim *and* agreement
            // between that claim and the host the request arrived on, so no request can reach tenant-scoped
            // data without a server-issued tenant identity that belongs to the workspace being addressed.
            options.DefaultPolicy = TenantScoped().Build();

            // Endpoints carrying no authorization attribute at all fall back to the same requirement, so
            // an endpoint added later is closed by default: forgetting [Authorize] can no longer publish
            // tenant data. Anonymous endpoints must say so explicitly with [AllowAnonymous].
            options.FallbackPolicy = options.DefaultPolicy;

            // One policy per known permission, named after the permission itself, so
            // [HasPermission(Permissions.Employee.View)] needs no extra registration.
            //
            // These are built from the same TenantScoped() base as the default policy, which matters more
            // than it looks: a named policy *replaces* the default policy rather than adding to it, so a
            // [HasPermission] endpoint never evaluates DefaultPolicy at all. Listing the tenant
            // requirements only above would have left every permission-guarded endpoint — which is to say
            // all the ones that touch employee data — with no host/token agreement check.
            //
            // Registered as a built policy rather than through the Action<AuthorizationPolicyBuilder>
            // overload: that overload configures a builder it supplies, so returning a policy from the
            // lambda silently discards it and registers a policy with no requirements at all.
            foreach (var permission in Permissions.All)
            {
                options.AddPolicy(
                    permission,
                    TenantScoped().RequireClaim(HrmsClaimTypes.Permission, permission).Build());
            }
        });

        // Scoped: the handler reads the shard resolved for this request.
        services.AddScoped<IAuthorizationHandler, TenantMatchesShardHandler>();

        // Turns a host/token disagreement into a 401 rather than the default 403.
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ShardMismatchAuthorizationResultHandler>();

        return services;
    }

    /// <summary>
    /// The requirements every tenant-scoped policy shares. A builder rather than a prebuilt policy so each
    /// caller can add its own requirements, and one definition so a policy cannot be registered that
    /// carries the permission check but forgets the tenant checks.
    /// </summary>
    private static AuthorizationPolicyBuilder TenantScoped() =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(HrmsClaimTypes.TenantId)
            .AddRequirements(new TenantMatchesShardRequirement());
}
