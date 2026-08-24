using HRMS.Application.DTOs.Designations;
using HRMS.Application.Validators.Common;

namespace HRMS.Application.Validators.Designations;

public class DesignationQueryValidator : PagedQueryValidator<DesignationQuery>
{
    public DesignationQueryValidator() : base(DesignationQuery.SortFields)
    {
    }
}
