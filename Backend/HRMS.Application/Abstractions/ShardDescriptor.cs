using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

/// <summary>
/// Everything a request needs to know about which organization it belongs to and which database holds
/// that organization's data. Produced by <see cref="ITenantShardResolver"/> from the catalog, carried for
/// the rest of the request by <see cref="IShardContext"/>.
/// <para>
/// <b>It deliberately does not carry a connection string.</b> A descriptor is cached, logged and — once
/// <c>SystemController</c> reports on tenants — potentially projected into a response, and a type that
/// holds credentials will eventually reach one of those places. It carries <see cref="ShardKey"/>
/// instead: a name that means nothing without the template and credentials held in configuration.
/// </para>
/// </summary>
/// <param name="TenantId">The organization's id. The value the JWT <c>tid</c> claim must agree with.</param>
/// <param name="TenantCode">The operator-facing label, for logs and support conversations.</param>
/// <param name="Host">The host that resolved to this organization, lowercase.</param>
/// <param name="ShardKey">Names the database holding this organization's data.</param>
/// <param name="Status">
/// Whether the organization may be served. Carried rather than filtered out at the source so the caller
/// can tell "no such workspace" from "this workspace is switched off" and log the two differently.
/// </param>
public sealed record ShardDescriptor(
    Guid TenantId,
    string TenantCode,
    string Host,
    string ShardKey,
    TenantStatus Status);
