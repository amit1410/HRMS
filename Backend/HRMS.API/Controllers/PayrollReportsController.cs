using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll/reports"), HasPermission(Permissions.Payroll.AnalyticsView)]
public sealed class PayrollReportsController(IPayrollReportsService service) : ControllerBase
{
    [HttpGet("payroll-register")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollReportRegisterRowDto>>>> Register([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetRegisterAsync(q, ct)).ToActionResult();
    [HttpGet("earnings")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollComponentReportRowDto>>>> Earnings([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetEarningsAsync(q, ct)).ToActionResult();
    [HttpGet("deductions")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollComponentReportRowDto>>>> Deductions([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetDeductionsAsync(q, ct)).ToActionResult();
    [HttpGet("employer-contributions")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollComponentReportRowDto>>>> EmployerContributions([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetEmployerContributionsAsync(q, ct)).ToActionResult();
    [HttpGet("departments")] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollDimensionReportRowDto>>>> Departments([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetDepartmentsAsync(q, ct)).ToActionResult();
    [HttpGet("cost-centers")] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollDimensionReportRowDto>>>> CostCenters([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetCostCentersAsync(q, ct)).ToActionResult();
    [HttpGet("components")] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollComponentSummaryRowDto>>>> Components([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetComponentsAsync(q, ct)).ToActionResult();
    [HttpGet("statutory-summary")] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollStatutorySummaryRowDto>>>> Statutory([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetStatutorySummaryAsync(q, ct)).ToActionResult();
    [HttpGet("bank-payments")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollBankPaymentReportRowDto>>>> BankPayments([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetBankPaymentsAsync(q, ct)).ToActionResult();
    [HttpGet("accounting-summary")] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollAccountingSummaryRowDto>>>> Accounting([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetAccountingSummaryAsync(q, ct)).ToActionResult();
    [HttpGet("loan-recoveries")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollLoanReportRowDto>>>> Loans([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetLoanRecoveriesAsync(q, ct)).ToActionResult();
    [HttpGet("reimbursements")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollReimbursementReportRowDto>>>> Reimbursements([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetReimbursementsAsync(q, ct)).ToActionResult();
    [HttpGet("variable-pay")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollVariablePayReportRowDto>>>> VariablePay([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetVariablePayAsync(q, ct)).ToActionResult();
    [HttpGet("adjustments")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollAdjustmentReportRowDto>>>> Adjustments([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetAdjustmentsAsync(q, ct)).ToActionResult();
    [HttpGet("final-settlements")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollFinalSettlementReportRowDto>>>> FinalSettlements([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetFinalSettlementsAsync(q, ct)).ToActionResult();
    [HttpGet("off-cycle")] public async Task<ActionResult<ApiResponse<PagedResult<PayrollOffCycleReportRowDto>>>> OffCycle([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetOffCycleAsync(q, ct)).ToActionResult();
    [HttpGet("dashboard")] public async Task<ActionResult<ApiResponse<PayrollDashboardDto>>> Dashboard([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetDashboardAsync(q, ct)).ToActionResult();
    [HttpGet("trends")] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollTrendRowDto>>>> Trends([FromQuery] PayrollReportQuery q, CancellationToken ct) => (await service.GetTrendsAsync(q, ct)).ToActionResult();
    [HttpGet("payroll-register.csv")] public async Task<IActionResult> RegisterCsv([FromQuery] PayrollReportQuery q, CancellationToken ct) { var result = await service.ExportRegisterAsync(q, ct); return result.Succeeded ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : BadRequest(result.Message); }
    [HttpGet("{report}.csv")] public async Task<IActionResult> Csv(string report, [FromQuery] PayrollReportQuery q, CancellationToken ct) { var result = await service.ExportAsync(report, q, ct); return result.Succeeded ? File(result.Value!.Content, result.Value.ContentType, result.Value.FileName) : BadRequest(result.Message); }
}
