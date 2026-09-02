namespace HRMS.Application.DTOs.Employees;

public class EmployeeSupervisorRequest
{
    public string? L1ManagerCode { get; set; }
    public string? L1ManagerName { get; set; }
    public Guid? L1ManagerId { get; set; }
    public string? L2ManagerCode { get; set; }
    public string? L2ManagerName { get; set; }
    public Guid? L2ManagerId { get; set; }
    public string? L3ManagerCode { get; set; }
    public string? L3ManagerName { get; set; }
    public Guid? L3ManagerId { get; set; }
    public string? L4ManagerCode { get; set; }
    public string? L4ManagerName { get; set; }
    public Guid? L4ManagerId { get; set; }
    public string? L5ManagerCode { get; set; }
    public string? L5ManagerName { get; set; }
    public Guid? L5ManagerId { get; set; }
    public string? TimeManagerCode { get; set; }
    public string? TimeManagerName { get; set; }
    public Guid? TimeManagerId { get; set; }
    public string? EroCode { get; set; }
    public string? EroName { get; set; }
    public Guid? EroId { get; set; }
    public string? ChroManagerCode { get; set; }
    public string? ChroManagerName { get; set; }
    public Guid? ChroManagerId { get; set; }
}
