using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;

namespace HRMS.Application.Abstractions;

public interface IPayrollReportsService
{
    Task<Result<PagedResult<PayrollReportRegisterRowDto>>> GetRegisterAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollComponentReportRowDto>>> GetEarningsAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollComponentReportRowDto>>> GetDeductionsAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollComponentReportRowDto>>> GetEmployerContributionsAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollDimensionReportRowDto>>> GetDepartmentsAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollDimensionReportRowDto>>> GetCostCentersAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollComponentSummaryRowDto>>> GetComponentsAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollStatutorySummaryRowDto>>> GetStatutorySummaryAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollBankPaymentReportRowDto>>> GetBankPaymentsAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollAccountingSummaryRowDto>>> GetAccountingSummaryAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollLoanReportRowDto>>> GetLoanRecoveriesAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollReimbursementReportRowDto>>> GetReimbursementsAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollVariablePayReportRowDto>>> GetVariablePayAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollAdjustmentReportRowDto>>> GetAdjustmentsAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollFinalSettlementReportRowDto>>> GetFinalSettlementsAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PagedResult<PayrollOffCycleReportRowDto>>> GetOffCycleAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PayrollDashboardDto>> GetDashboardAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PayrollTrendRowDto>>> GetTrendsAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportRegisterAsync(PayrollReportQuery query, CancellationToken ct = default);
    Task<Result<PayrollOutputFile>> ExportAsync(string report, PayrollReportQuery query, CancellationToken ct = default);
}
