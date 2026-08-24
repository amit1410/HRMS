using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

// Both namespaces above declare an IPNetwork. The alias picks the framework one deliberately: the
// HttpOverrides type is obsolete, and KnownIPNetworks — the property that is not — takes this one.
using IPNetwork = System.Net.IPNetwork;

namespace HRMS.API.Security;

/// <summary>
/// Configures which forwarded headers this API honours, and from whom.
/// </summary>
public static class ForwardedHeadersServiceCollectionExtensions
{
    /// <summary>
    /// Honours <c>X-Forwarded-For</c>, <c>-Proto</c> and <c>-Host</c> from trusted proxies only.
    /// <para>
    /// All three are needed and each for its own reason: <c>-For</c> so the rate limiter partitions and the
    /// logs record the caller rather than the load balancer, <c>-Proto</c> so HTTPS redirection does not
    /// bounce a request that already arrived over TLS at the edge, and <c>-Host</c> so tenant resolution sees
    /// the address the browser was pointed at instead of an internal service name that resolves to nothing.
    /// </para>
    /// <para>
    /// The framework's loopback defaults are extended, never cleared. Clearing <c>KnownProxies</c> and
    /// <c>KnownNetworks</c> is the widely-copied snippet for "make it work behind my proxy", and what it
    /// actually does is believe these headers from any source at all — which, once the host picks the
    /// database, is a way to ask for a different tenant's shard by adding a header.
    /// </para>
    /// </summary>
    public static IServiceCollection AddHrmsForwardedHeaders(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration.GetSection(ForwardedHeadersSettings.SectionName)
            .Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();

        // Parsed here so a malformed address stops startup. Left to the options callback it would surface as
        // a request-time exception, and a deployment that silently dropped its proxy would look like a
        // working deployment logging every caller as the load balancer.
        var knownProxies = Parse(
            settings.KnownProxies,
            nameof(ForwardedHeadersSettings.KnownProxies),
            value => IPAddress.Parse(value));

        var knownNetworks = Parse(
            settings.KnownNetworks,
            nameof(ForwardedHeadersSettings.KnownNetworks),
            value => IPNetwork.Parse(value));

        if (settings.ForwardLimit < 1)
        {
            throw new InvalidOperationException(
                $"'{ForwardedHeadersSettings.SectionName}:{nameof(ForwardedHeadersSettings.ForwardLimit)}' "
                + $"must be at least 1, but was {settings.ForwardLimit}. To stop honouring forwarded headers "
                + "entirely, remove the proxy from the trusted lists instead.");
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

            options.ForwardLimit = settings.ForwardLimit;

            foreach (var proxy in knownProxies)
            {
                options.KnownProxies.Add(proxy);
            }

            foreach (var network in knownNetworks)
            {
                options.KnownIPNetworks.Add(network);
            }

            // AllowedHosts is left empty on purpose, which means any forwarded host is accepted. Restricting
            // it here would be a second allow-list of tenant addresses that has to be edited and redeployed
            // per customer; the shard catalog already refuses a host it does not know, and it learns a new
            // one the moment an organization is provisioned.
        });

        return services;
    }

    private static List<T> Parse<T>(string[]? values, string key, Func<string, T> parse)
    {
        var parsed = new List<T>();

        foreach (var value in values ?? [])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            try
            {
                parsed.Add(parse(value.Trim()));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                throw new InvalidOperationException(
                    $"'{ForwardedHeadersSettings.SectionName}:{key}' entry '{value}' could not be read as an "
                    + "address or range.", ex);
            }
        }

        return parsed;
    }
}
