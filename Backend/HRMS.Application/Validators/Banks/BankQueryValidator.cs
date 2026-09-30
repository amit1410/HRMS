using HRMS.Application.DTOs.Banks;
using HRMS.Application.Validators.Common;

namespace HRMS.Application.Validators.Banks;

public class BankQueryValidator : PagedQueryValidator<BankQuery>
{
    public BankQueryValidator() : base(BankQuery.SortFields)
    {
    }
}
