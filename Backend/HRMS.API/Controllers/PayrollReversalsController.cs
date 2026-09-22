using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll/reversals")]
public sealed class PayrollReversalsController(IPayrollAdjustmentService service) : ControllerBase
{
    [HttpPost, HasPermission(Permissions.Payroll.AdjustmentsReverse)] public async Task<ActionResult<ApiResponse<PayrollReversalDto>>> RequestReversal(PayrollReversalRequest request, CancellationToken ct) => (await service.RequestReversalAsync(request, ct)).ToActionResult();
    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.Payroll.AdjustmentsReverse)] public async Task<ActionResult<ApiResponse<PayrollReversalDto>>> Approve(Guid id, CancellationToken ct) => (await service.ApproveReversalAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/process"), HasPermission(Permissions.Payroll.AdjustmentsReverse)] public async Task<ActionResult<ApiResponse<PayrollReversalDto>>> Process(Guid id, CancellationToken ct) => (await service.ProcessReversalAsync(id, ct)).ToActionResult();
}
