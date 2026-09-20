using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollReadinessService
{
    Task<Result<PayrollReadinessDto>> CheckAsync(Guid payrollRunId, CancellationToken ct = default);
}
