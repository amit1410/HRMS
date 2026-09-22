using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll/off-cycle-runs")]
public sealed class PayrollOffCycleController(IPayrollAdjustmentService service) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.OffCycleView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollRunDto>>>> Get([FromQuery] PayrollRunQuery query, CancellationToken ct) => (await service.GetOffCycleRunsAsync(query, ct)).ToActionResult();
    [HttpPost, HasPermission(Permissions.Payroll.OffCycleCreate)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Create(PayrollOffCycleRunRequest request, CancellationToken ct) => (await service.CreateOffCycleRunAsync(request, ct)).ToActionResult();
    [HttpPost("{id:guid}/preview"), HasPermission(Permissions.Payroll.OffCycleView)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Preview(Guid id, CancellationToken ct) => (await service.PreviewOffCycleRunAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/prepare"), HasPermission(Permissions.Payroll.OffCycleCreate)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Prepare(Guid id, CancellationToken ct) => (await service.PrepareOffCycleRunAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.Payroll.OffCycleApprove)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Approve(Guid id, CancellationToken ct) => (await service.ApproveOffCycleRunAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/process"), HasPermission(Permissions.Payroll.OffCycleProcess)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Process(Guid id, CancellationToken ct) => (await service.ProcessOffCycleRunAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.Payroll.OffCycleCancel)] public async Task<ActionResult<ApiResponse<PayrollRunDto>>> Cancel(Guid id, [FromBody] string reason, CancellationToken ct) => (await service.CancelOffCycleRunAsync(id, reason, ct)).ToActionResult();
}
