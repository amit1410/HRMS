using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollOperationsService
{
    Task<Result<PayrollConfigurationHealthDto>> GetConfigurationHealthAsync(CancellationToken ct = default);
    Task<Result<PayrollOperationsDashboardDto>> GetOperationsDashboardAsync(CancellationToken ct = default);
}
