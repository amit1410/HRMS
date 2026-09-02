using HRMS.Domain.Common;

namespace HRMS.Domain.Entities;

/// <summary>
/// Supervisor hierarchy for an employee. Supports up to 5 levels of manager escalation
/// plus specialized manager roles (Time Manager, ERO, CHRO Manager).
/// </summary>
public class EmployeeSupervisor : BaseEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid EmployeeId { get; set; }

    /// <summary>L1 manager employee code (direct supervisor).</summary>
    public string? L1ManagerCode { get; set; }

    public string? L1ManagerName { get; set; }

    public Guid? L1ManagerId { get; set; }

    /// <summary>L2 manager employee code.</summary>
    public string? L2ManagerCode { get; set; }

    public string? L2ManagerName { get; set; }

    public Guid? L2ManagerId { get; set; }

    /// <summary>L3 manager employee code.</summary>
    public string? L3ManagerCode { get; set; }

    public string? L3ManagerName { get; set; }

    public Guid? L3ManagerId { get; set; }

    /// <summary>L4 manager employee code.</summary>
    public string? L4ManagerCode { get; set; }

    public string? L4ManagerName { get; set; }

    public Guid? L4ManagerId { get; set; }

    /// <summary>L5 manager employee code.</summary>
    public string? L5ManagerCode { get; set; }

    public string? L5ManagerName { get; set; }

    public Guid? L5ManagerId { get; set; }

    /// <summary>Time manager employee code.</summary>
    public string? TimeManagerCode { get; set; }

    public string? TimeManagerName { get; set; }

    public Guid? TimeManagerId { get; set; }

    /// <summary>ERO (Employee Relations Officer) employee code.</summary>
    public string? EroCode { get; set; }

    public string? EroName { get; set; }

    public Guid? EroId { get; set; }

    /// <summary>CHRO (Chief HR Officer) manager employee code.</summary>
    public string? ChroManagerCode { get; set; }

    public string? ChroManagerName { get; set; }

    public Guid? ChroManagerId { get; set; }

    // Navigation
    public Tenant? Tenant { get; set; }
    public Employee? Employee { get; set; }
}
