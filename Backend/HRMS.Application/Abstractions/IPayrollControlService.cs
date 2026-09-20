using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollControlService
{
    Task<Result<PayrollControlConfigurationDto>> GetAsync(CancellationToken ct = default);
    Task<Result<PayrollControlConfigurationDto>> UpdateAsync(PayrollControlConfigurationRequest request, CancellationToken ct = default);
}
