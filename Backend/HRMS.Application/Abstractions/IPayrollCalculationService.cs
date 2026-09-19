using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollCalculationEngine
{
    Task<Result<PayrollCalculationSummaryDto>> CalculateAsync(Guid payrollRunId, bool recalculate, CancellationToken ct = default);
}

public interface IPayrollCalculationService
{
    Task<Result<PayrollCalculationSummaryDto>> CalculateAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<PayrollCalculationSummaryDto>> RecalculateAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollResultDto>>> GetResultsAsync(Guid payrollRunId, PayrollResultQuery query, CancellationToken ct = default);
    Task<Result<PayrollResultDto>> GetResultAsync(Guid payrollRunId, Guid employeeId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollCalculationErrorDto>>> GetErrorsAsync(Guid payrollRunId, CancellationToken ct = default);
}
