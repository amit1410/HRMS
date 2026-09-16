using HRMS.Domain.Common;
using HRMS.Domain.Enums;

namespace HRMS.Domain.Entities;

public sealed class UserRoleAssignmentScope : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserRoleAssignmentId { get; set; }
    public RoleScopeType ScopeType { get; set; }
    public Guid ScopeEntityId { get; set; }

    public UserRole? Assignment { get; set; }
}
