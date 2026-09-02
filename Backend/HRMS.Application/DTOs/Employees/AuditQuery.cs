using HRMS.Application.Common;
using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public class AuditQuery : PagedQuery
{
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public string? Module { get; set; }
    public string? Section { get; set; }
    public AuditChangeType? ChangeType { get; set; }
    public string? User { get; set; }
}
