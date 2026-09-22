using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll/adjustments")]
public sealed class PayrollAdjustmentsController(IPayrollAdjustmentService service, IAccountEmployeeLinkService links, ITenantContext tenant) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Payroll.AdjustmentsView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollAdjustmentDto>>>> Get([FromQuery] PayrollAdjustmentQuery query, CancellationToken ct) => (await service.GetAsync(query, ct)).ToActionResult();
    [HttpGet("{id:guid}"), HasPermission(Permissions.Payroll.AdjustmentsView)] public async Task<ActionResult<ApiResponse<PayrollAdjustmentDto>>> GetById(Guid id, CancellationToken ct) => (await service.GetByIdAsync(id, ct)).ToActionResult();
    [HttpPost, HasPermission(Permissions.Payroll.AdjustmentsCreate)] public async Task<ActionResult<ApiResponse<PayrollAdjustmentDto>>> Create(PayrollAdjustmentRequest request, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToCreatedResult(nameof(GetById), x => new { id = x.Id });
    [HttpPost("{id:guid}/submit"), HasPermission(Permissions.Payroll.AdjustmentsSubmit)] public async Task<ActionResult<ApiResponse<PayrollAdjustmentDto>>> Submit(Guid id, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/approve"), HasPermission(Permissions.Payroll.AdjustmentsApprove)] public async Task<ActionResult<ApiResponse<PayrollAdjustmentDto>>> Approve(Guid id, CancellationToken ct) => (await service.ApproveAsync(id, ct)).ToActionResult();
    [HttpPost("{id:guid}/reject"), HasPermission(Permissions.Payroll.AdjustmentsApprove)] public async Task<ActionResult<ApiResponse<PayrollAdjustmentDto>>> Reject(Guid id, [FromBody] string reason, CancellationToken ct) => (await service.RejectAsync(id, reason, ct)).ToActionResult();
    [HttpPost("{id:guid}/cancel"), HasPermission(Permissions.Payroll.AdjustmentsCancel)] public async Task<ActionResult<ApiResponse<PayrollAdjustmentDto>>> Cancel(Guid id, [FromBody] string reason, CancellationToken ct) => (await service.CancelAsync(id, reason, ct)).ToActionResult();
    [HttpGet("{id:guid}/history"), HasPermission(Permissions.Payroll.AdjustmentsViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollAdjustmentHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();
    [HttpGet("reasons"), HasPermission(Permissions.Payroll.AdjustmentsView)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollAdjustmentReasonDto>>>> Reasons(CancellationToken ct) => (await service.GetReasonsAsync(ct)).ToActionResult();
    [HttpPost("reasons"), HasPermission(Permissions.Payroll.AdjustmentsCreate)] public async Task<ActionResult<ApiResponse<PayrollAdjustmentReasonDto>>> CreateReason(PayrollAdjustmentReasonRequest request, CancellationToken ct) => (await service.CreateReasonAsync(request, ct)).ToActionResult();

    [HttpGet("/api/me/payroll-adjustments"), HasPermission(Permissions.Payroll.AdjustmentsView)] public async Task<ActionResult<ApiResponse<PagedResult<PayrollAdjustmentDto>>>> My([FromQuery] PayrollAdjustmentQuery query, CancellationToken ct)
    {
        if (tenant.UserId is not Guid userId) return Unauthorized();
        var identity = await links.GetUserAsync(userId, ct);
        if (!identity.Succeeded || identity.Value?.CurrentLink is null) return Unauthorized();
        query.EmployeeId = identity.Value.CurrentLink.EmployeeId;
        return (await service.GetAsync(query, ct)).ToActionResult();
    }

    [HttpGet("/api/me/payroll-adjustments/{id:guid}"), HasPermission(Permissions.Payroll.AdjustmentsView)] public async Task<ActionResult<ApiResponse<PayrollAdjustmentDto>>> MyDetail(Guid id, CancellationToken ct)
    {
        if (tenant.UserId is not Guid userId) return Unauthorized();
        var identity = await links.GetUserAsync(userId, ct);
        if (!identity.Succeeded || identity.Value?.CurrentLink is null) return Unauthorized();
        var result = await service.GetByIdAsync(id, ct);
        if (!result.Succeeded || result.Value?.EmployeeId != identity.Value.CurrentLink.EmployeeId) return NotFound();
        return result.ToActionResult();
    }

    [HttpGet("/api/me/payroll-adjustments/{id:guid}/history"), HasPermission(Permissions.Payroll.AdjustmentsViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollAdjustmentHistoryDto>>>> MyHistory(Guid id, CancellationToken ct)
    {
        if (tenant.UserId is not Guid userId) return Unauthorized();
        var identity = await links.GetUserAsync(userId, ct);
        if (!identity.Succeeded || identity.Value?.CurrentLink is null) return Unauthorized();
        var adjustment = await service.GetByIdAsync(id, ct);
        if (!adjustment.Succeeded || adjustment.Value?.EmployeeId != identity.Value.CurrentLink.EmployeeId) return NotFound();
        return (await service.GetHistoryAsync(id, ct)).ToActionResult();
    }
}
