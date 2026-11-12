using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>Immutable audit snapshot for changes to a tenant's employee-code configuration version.</summary>
public sealed class EmployeeCodeConfigurationAuditEvent : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeCodeConfigId { get; set; }
    public Guid EmployeeCodeConfigVersionId { get; set; }
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public string? OldValue { get; set; }
    public string NewValue { get; set; } = string.Empty;
}
