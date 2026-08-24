namespace HRMS.API.Security;

/// <summary>
/// Binding for the "ForwardedHeaders" configuration section: which proxies this API believes when they
/// rewrite the client address, the scheme and the host.
/// <para>
/// This matters more here than in a single-tenant API. <c>X-Forwarded-Host</c> now decides which
/// <em>database</em> a request opens, because host resolution is how a tenant is chosen. A deployment that
/// trusted that header from anywhere would let any caller name any organization's host and be routed to its
/// shard — so the defaults trust nothing but loopback, and every widening is written down here.
/// </para>
/// <para>
/// One case is not a trust decision at all: when the transport reports no peer address there is nothing to
/// compare against these lists, and the headers are honoured. That happens over a Unix socket or a named
/// pipe — where the peer is necessarily on this machine — and in the test host. Over TCP an address is always
/// present, so this reaches no deployment that does not already run its proxy locally.
/// </para>
/// </summary>
public sealed class ForwardedHeadersSettings
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Addresses of the proxies in front of this API, in addition to loopback. Empty is correct when the
    /// proxy shares the host (a sidecar, or IIS in-process); a proxy on another machine has to be listed or
    /// its headers are ignored and every request appears to come from the proxy itself over http.
    /// </summary>
    public string[] KnownProxies { get; set; } = [];

    /// <summary>
    /// CIDR ranges to trust, for load balancers whose address is not fixed — <c>10.0.0.0/8</c>. Prefer
    /// <see cref="KnownProxies"/>: a range trusts everything in it, and in a shared network that is more
    /// hosts than the ones doing the proxying.
    /// </summary>
    public string[] KnownNetworks { get; set; } = [];

    /// <summary>
    /// How many entries to read from the right of each forwarded header — one per proxy in the chain, and no
    /// more. Raising it past the real chain length lets the client append its own entry and have it believed.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;
}
