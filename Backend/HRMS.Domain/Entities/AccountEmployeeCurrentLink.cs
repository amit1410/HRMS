using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

public sealed class AccountEmployeeCurrentLink : ITenantEntity
{
    public Guid LinkId { get; set; }
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public Guid EmployeeId { get; set; }

    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
    public Employee? Employee { get; set; }
    public AccountEmployeeLinkEvent? CreationEvent { get; set; }
}
