using HRMS.Application.DTOs.Departments;
using HRMS.Application.Validators.Common;

namespace HRMS.Application.Validators.Departments;

public class DepartmentQueryValidator : PagedQueryValidator<DepartmentQuery>
{
    public DepartmentQueryValidator() : base(DepartmentQuery.SortFields)
    {
    }
}
