namespace HRMS.API.Security;

/// <summary>Named CORS policies referenced by <c>UseCors</c>.</summary>
public static class CorsPolicies
{
    /// <summary>
    /// The one policy this API applies. Browser clients are the only cross-origin callers it has, and they
    /// all get the same answer, so a second policy would only be a second place for the two to drift.
    /// </summary>
    public const string Client = "HrmsCorsPolicy";
}

/// <summary>
/// Registers the CORS policy, built from <see cref="CorsSettings"/>.
/// </summary>
public static class HrmsCorsServiceCollectionExtensions
{
    /// <summary>
    /// Adds the HRMS CORS policy: credentialed cross-origin access for configured exact origins and for any
    /// single workspace label under a configured whitelabel template.
    /// <para>
    /// The origin set is resolved here, at registration, and is therefore fixed until the process restarts —
    /// <c>AddPolicy</c> runs its configure action once and caches the result, so a policy that re-read
    /// configuration per request would need a different mechanism entirely. That is an acceptable trade
    /// because onboarding an organization needs no CORS change at all; only moving to a new base domain does,
    /// and that is a deployment event either way.
    /// </para>
    /// </summary>
    public static IServiceCollection AddHrmsCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Built eagerly so a malformed origin or template stops startup here, with the offending entry
        // quoted, rather than on the first preflight of the day.
        var originPolicy = CorsOriginPolicy.FromConfiguration(configuration);

        // Resolvable so tests and diagnostics can ask the same object the pipeline asks.
        services.AddSingleton(originPolicy);

        services.AddCors(options => options.AddPolicy(CorsPolicies.Client, policy => policy
            // A predicate rather than WithOrigins, because the allowed set is open-ended by design: one
            // template stands for every organization's address. ASP.NET Core still echoes the single
            // requesting origin back, so this is not a wildcard response — it is an allow-list whose
            // membership is computed.
            .SetIsOriginAllowed(originPolicy.IsAllowed)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            // Content-Disposition is not a CORS-safelisted response header, so without this the browser
            // hides it from the client and the employee CSV export downloads under a generated name
            // instead of the one this API chose.
            .WithExposedHeaders("Content-Disposition")));

        return services;
    }
}
