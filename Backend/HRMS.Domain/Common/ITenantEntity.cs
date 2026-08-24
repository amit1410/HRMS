namespace HRMS.Domain.Common;

/// <summary>
/// Marker for entities that belong to a single tenant. Every tenant-scoped entity carries a
/// <see cref="TenantId"/> which is enforced server-side via EF Core global query filters and a
/// SaveChanges guard — it is never trusted from client input.
/// </summary>
public interface ITenantEntity
{
    Guid TenantId { get; set; }
}
