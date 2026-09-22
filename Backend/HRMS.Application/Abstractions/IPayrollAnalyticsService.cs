using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Enums;

namespace HRMS.Application.Abstractions;

public interface IPayrollAnalyticsService
{
    Task<Result<PayrollAnalyticsOverviewDto>> GetOverviewAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<PayrollRunSummaryDto>> GetRunSummaryAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<PayrollControlTotalsDto>> GetControlTotalsAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollDimensionSummaryDto>>> GetDepartmentSummaryAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollDimensionSummaryDto>>> GetCostCenterSummaryAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollExceptionDto>>> GetExceptionsAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollVarianceRowDto>>> GetVarianceAsync(Guid payrollRunId, Guid? compareRunId, PagedQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollComponentVarianceDto>>> GetComponentVarianceAsync(Guid payrollRunId, Guid? compareRunId, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollFindingDto>>> GetFindingsAsync(Guid payrollRunId, PagedQuery query, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportVarianceAsync(Guid payrollRunId, Guid? compareRunId, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportFindingsAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportRunSummaryAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportControlTotalsAsync(Guid payrollRunId, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportExceptionsAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<PayrollReconciliationDto>> GenerateReconciliationAsync(Guid payrollRunId, PayrollReconciliationType type, CancellationToken ct = default);
    Task<Result<PayrollReconciliationDto>> GetReconciliationAsync(Guid id, CancellationToken ct = default);
    Task<Result<PayrollReconciliationDto>> ActOnFindingAsync(Guid findingId, string action, PayrollFindingActionRequest request, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollAnalyticsControlDto>>> ListControlsAsync(PagedQuery query, CancellationToken ct = default);
    Task<Result<PayrollAnalyticsControlDto>> CreateControlAsync(PayrollAnalyticsControlRequest request, CancellationToken ct = default);
    Task<Result<PayrollAnalyticsControlDto>> UpdateControlAsync(Guid id, PayrollAnalyticsControlRequest request, CancellationToken ct = default);
}
