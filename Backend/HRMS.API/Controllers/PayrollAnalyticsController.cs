using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll/analytics")]
public sealed class PayrollAnalyticsController(IPayrollAnalyticsService service) : ControllerBase
{
    [HttpGet("runs/{runId:guid}/overview"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<ActionResult<ApiResponse<PayrollAnalyticsOverviewDto>>> Overview(Guid runId, CancellationToken ct) => (await service.GetOverviewAsync(runId, ct)).ToActionResult();
    [HttpGet("runs/{runId:guid}/summary"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<ActionResult<ApiResponse<PayrollRunSummaryDto>>> Summary(Guid runId, CancellationToken ct) => (await service.GetRunSummaryAsync(runId, ct)).ToActionResult();
    [HttpGet("runs/{runId:guid}/control-totals"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<ActionResult<ApiResponse<PayrollControlTotalsDto>>> ControlTotals(Guid runId, CancellationToken ct) => (await service.GetControlTotalsAsync(runId, ct)).ToActionResult();
    [HttpGet("runs/{runId:guid}/departments"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollDimensionSummaryDto>>>> Departments(Guid runId, CancellationToken ct) => (await service.GetDepartmentSummaryAsync(runId, ct)).ToActionResult();
    [HttpGet("runs/{runId:guid}/cost-centers"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollDimensionSummaryDto>>>> CostCenters(Guid runId, CancellationToken ct) => (await service.GetCostCenterSummaryAsync(runId, ct)).ToActionResult();
    [HttpGet("runs/{runId:guid}/variance"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollVarianceRowDto>>>> Variance(Guid runId, Guid? compareRunId, [FromQuery] PagedQuery query, CancellationToken ct) => (await service.GetVarianceAsync(runId, compareRunId, query, ct)).ToActionResult();
    [HttpGet("runs/{runId:guid}/components"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollComponentVarianceDto>>>> Components(Guid runId, Guid? compareRunId, CancellationToken ct) => (await service.GetComponentVarianceAsync(runId, compareRunId, ct)).ToActionResult();
    [HttpGet("runs/{runId:guid}/findings"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollFindingDto>>>> Findings(Guid runId, [FromQuery] PagedQuery query, CancellationToken ct) => (await service.GetFindingsAsync(runId, query, ct)).ToActionResult();
    [HttpGet("runs/{runId:guid}/variance.csv"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<IActionResult> VarianceCsv(Guid runId, Guid? compareRunId, CancellationToken ct) { var result = await service.ExportVarianceAsync(runId, compareRunId, ct); if (!result.Succeeded) return BadRequest(result.Message); return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName); }
    [HttpGet("runs/{runId:guid}/findings.csv"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<IActionResult> FindingsCsv(Guid runId, CancellationToken ct) { var result = await service.ExportFindingsAsync(runId, ct); if (!result.Succeeded) return BadRequest(result.Message); return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName); }
    [HttpGet("runs/{runId:guid}/summary.csv"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<IActionResult> SummaryCsv(Guid runId, CancellationToken ct) { var result = await service.ExportRunSummaryAsync(runId, ct); if (!result.Succeeded) return BadRequest(result.Message); return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName); }
    [HttpGet("runs/{runId:guid}/control-totals.csv"), HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<IActionResult> ControlTotalsCsv(Guid runId, CancellationToken ct) { var result = await service.ExportControlTotalsAsync(runId, ct); if (!result.Succeeded) return BadRequest(result.Message); return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName); }
}

[ApiController, Route("api/payroll/exceptions")]
public sealed class PayrollExceptionsController(IPayrollAnalyticsService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.ExceptionsView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollExceptionDto>>>> List([FromQuery] PagedQuery query, CancellationToken ct) => (await service.GetExceptionsAsync(query, ct)).ToActionResult();
    [HttpGet("export.csv"), HasPermission(Permissions.Payroll.ExceptionsView)] public async Task<IActionResult> Export([FromQuery] PagedQuery query, CancellationToken ct) { var result = await service.ExportExceptionsAsync(query, ct); if (!result.Succeeded) return BadRequest(result.Message); return File(result.Value!.Content, result.Value.ContentType, result.Value.FileName); }
}

[ApiController, Route("api/payroll/reconciliation")]
public sealed class PayrollReconciliationController(IPayrollAnalyticsService service) : ControllerBase
{
    [HttpPost("runs/{runId:guid}/pre"), HasPermission(Permissions.Payroll.ReconciliationGenerate)] public async Task<ActionResult<ApiResponse<PayrollReconciliationDto>>> Pre(Guid runId, CancellationToken ct) => (await service.GenerateReconciliationAsync(runId, PayrollReconciliationType.PrePayroll, ct)).ToActionResult();
    [HttpPost("runs/{runId:guid}/post"), HasPermission(Permissions.Payroll.ReconciliationGenerate)] public async Task<ActionResult<ApiResponse<PayrollReconciliationDto>>> Post(Guid runId, CancellationToken ct) => (await service.GenerateReconciliationAsync(runId, PayrollReconciliationType.PostPayroll, ct)).ToActionResult();
    [HttpGet("{id:guid}"), HasPermission(Permissions.Payroll.ReconciliationView)] public async Task<ActionResult<ApiResponse<PayrollReconciliationDto>>> Get(Guid id, CancellationToken ct) => (await service.GetReconciliationAsync(id, ct)).ToActionResult();
    [HttpPost("findings/{id:guid}/acknowledge"), HasPermission(Permissions.Payroll.ReconciliationAcknowledge)] public async Task<ActionResult<ApiResponse<PayrollReconciliationDto>>> Acknowledge(Guid id, PayrollFindingActionRequest request, CancellationToken ct) => (await service.ActOnFindingAsync(id, "acknowledge", request, ct)).ToActionResult();
    [HttpPost("findings/{id:guid}/resolve"), HasPermission(Permissions.Payroll.ReconciliationResolve)] public async Task<ActionResult<ApiResponse<PayrollReconciliationDto>>> Resolve(Guid id, PayrollFindingActionRequest request, CancellationToken ct) => (await service.ActOnFindingAsync(id, "resolve", request, ct)).ToActionResult();
    [HttpPost("findings/{id:guid}/accept-exception"), HasPermission(Permissions.Payroll.ReconciliationResolve)] public async Task<ActionResult<ApiResponse<PayrollReconciliationDto>>> Accept(Guid id, PayrollFindingActionRequest request, CancellationToken ct) => (await service.ActOnFindingAsync(id, "accept-exception", request, ct)).ToActionResult();
}

[ApiController, Route("api/payroll/analytics-controls")]
public sealed class PayrollAnalyticsControlsController(IPayrollAnalyticsService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.AnalyticsView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollAnalyticsControlDto>>>> List([FromQuery] PagedQuery query, CancellationToken ct) => (await service.ListControlsAsync(query, ct)).ToActionResult();
    [HttpPost, HasPermission(Permissions.Payroll.AnalyticsConfigure)] public async Task<ActionResult<ApiResponse<PayrollAnalyticsControlDto>>> Create(PayrollAnalyticsControlRequest request, CancellationToken ct) => (await service.CreateControlAsync(request, ct)).ToActionResult();
    [HttpPut("{id:guid}"), HasPermission(Permissions.Payroll.AnalyticsConfigure)] public async Task<ActionResult<ApiResponse<PayrollAnalyticsControlDto>>> Update(Guid id, PayrollAnalyticsControlRequest request, CancellationToken ct) => (await service.UpdateControlAsync(id, request, ct)).ToActionResult();
}
