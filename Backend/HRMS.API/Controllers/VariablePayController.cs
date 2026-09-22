using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll")]
public sealed class VariablePayController(IVariablePayService service, IAccountEmployeeLinkService links, ITenantContext tenant) : ControllerBase
{
    [HttpGet("variable-pay-plans"), HasPermission(Permissions.Payroll.VariablePayView)] public async Task<ActionResult<ApiResponse<IReadOnlyList<VariablePayPlanDto>>>> Plans(CancellationToken ct) => (await service.GetPlansAsync(ct)).ToActionResult();
    [HttpPost("variable-pay-plans"), HasPermission(Permissions.Payroll.VariablePayManagePlans)] public async Task<ActionResult<ApiResponse<VariablePayPlanDto>>> CreatePlan(VariablePayPlanRequest request, CancellationToken ct) => (await service.CreatePlanAsync(request, ct)).ToActionResult();
    [HttpPut("variable-pay-plans/{id:guid}"), HasPermission(Permissions.Payroll.VariablePayManagePlans)] public async Task<ActionResult<ApiResponse<VariablePayPlanDto>>> UpdatePlan(Guid id, VariablePayPlanRequest request, CancellationToken ct) => (await service.UpdatePlanAsync(id, request, ct)).ToActionResult();
    [HttpPost("variable-pay-plans/{id:guid}/versions"), HasPermission(Permissions.Payroll.VariablePayManagePlans)] public async Task<ActionResult<ApiResponse<VariablePayPlanDto>>> AddVersion(Guid id, VariablePayPlanVersionRequest request, CancellationToken ct) => (await service.AddVersionAsync(id, request, ct)).ToActionResult();
    [HttpGet("variable-pay-awards"), HasPermission(Permissions.Payroll.VariablePayView)] public async Task<ActionResult<ApiResponse<PagedResult<VariablePayAwardDto>>>> Awards([FromQuery] VariablePayAwardQuery query, CancellationToken ct) => (await service.GetAwardsAsync(query, ct)).ToActionResult();
    [HttpGet("variable-pay-awards/{id:guid}"), HasPermission(Permissions.Payroll.VariablePayView)] public async Task<ActionResult<ApiResponse<VariablePayAwardDto>>> Get(Guid id, CancellationToken ct) => (await service.GetAsync(id, ct)).ToActionResult();
    [HttpPost("variable-pay-awards/generate-preview"), HasPermission(Permissions.Payroll.VariablePayCalculate)] public async Task<ActionResult<ApiResponse<VariablePayAwardDto>>> Preview(Guid employeeId, VariablePayAwardRequest request, CancellationToken ct) => (await service.PreviewAsync(employeeId, request, ct)).ToActionResult();
    [HttpPost("variable-pay-awards/generate"), HasPermission(Permissions.Payroll.VariablePayCreateAward)] public async Task<ActionResult<ApiResponse<VariablePayAwardDto>>> Generate(Guid employeeId, VariablePayAwardRequest request, CancellationToken ct) => (await service.GenerateAsync(employeeId, request, ct)).ToActionResult();
    [HttpPost("variable-pay-awards/generate-bulk"), HasPermission(Permissions.Payroll.VariablePayCreateAward)] public async Task<ActionResult<ApiResponse<VariablePayBulkGenerationResult>>> GenerateBulk(VariablePayBulkGenerationRequest request, CancellationToken ct) => (await service.GenerateBulkAsync(request, ct)).ToActionResult();
    [HttpPost("variable-pay-awards/{id:guid}/submit"), HasPermission(Permissions.Payroll.VariablePaySubmit)] public async Task<ActionResult<ApiResponse<VariablePayAwardDto>>> Submit(Guid id, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToActionResult();
    [HttpPost("variable-pay-awards/{id:guid}/approve"), HasPermission(Permissions.Payroll.VariablePayApprove)] public async Task<ActionResult<ApiResponse<VariablePayAwardDto>>> Approve(Guid id, CancellationToken ct) => (await service.ApproveAsync(id, ct)).ToActionResult();
    [HttpPost("variable-pay-awards/{id:guid}/reject"), HasPermission(Permissions.Payroll.VariablePayApprove)] public async Task<ActionResult<ApiResponse<VariablePayAwardDto>>> Reject(Guid id, [FromBody] string reason, CancellationToken ct) => (await service.RejectAsync(id, reason, ct)).ToActionResult();
    [HttpPost("variable-pay-awards/{id:guid}/override"), HasPermission(Permissions.Payroll.VariablePayOverride)] public async Task<ActionResult<ApiResponse<VariablePayAwardDto>>> Override(Guid id, VariablePayOverrideRequest request, CancellationToken ct) => (await service.OverrideAsync(id, request, ct)).ToActionResult();
    [HttpPost("variable-pay-awards/{id:guid}/cancel"), HasPermission(Permissions.Payroll.VariablePayCancel)] public async Task<ActionResult<ApiResponse<VariablePayAwardDto>>> Cancel(Guid id, [FromBody] string reason, CancellationToken ct) => (await service.CancelAsync(id, reason, ct)).ToActionResult();
    [HttpPost("variable-pay-awards/{id:guid}/settle"), HasPermission(Permissions.Payroll.VariablePayApprove)] public async Task<ActionResult<ApiResponse<VariablePayAwardDto>>> Settle(Guid id, VariablePaySettlementRequest request, CancellationToken ct) => (await service.SettleAsync(id, request, ct)).ToActionResult();
    [HttpGet("variable-pay-awards/{id:guid}/history"), HasPermission(Permissions.Payroll.VariablePayViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<VariablePayAwardHistoryDto>>>> History(Guid id, CancellationToken ct) => (await service.GetHistoryAsync(id, ct)).ToActionResult();

    [HttpGet("/api/me/variable-pay"), HasPermission(Permissions.Payroll.VariablePayView)] public async Task<ActionResult<ApiResponse<PagedResult<VariablePayAwardDto>>>> My([FromQuery] VariablePayAwardQuery query, CancellationToken ct)
    {
        if (tenant.UserId is not Guid userId) return Unauthorized(); var identity = await links.GetUserAsync(userId, ct); if (!identity.Succeeded || identity.Value?.CurrentLink is null) return Unauthorized(); query.EmployeeId = identity.Value.CurrentLink.EmployeeId; return (await service.GetAwardsAsync(query, ct)).ToActionResult();
    }

    [HttpGet("/api/me/variable-pay/{id:guid}"), HasPermission(Permissions.Payroll.VariablePayView)] public async Task<ActionResult<ApiResponse<VariablePayAwardDto>>> MyDetail(Guid id, CancellationToken ct)
    {
        if (tenant.UserId is not Guid userId) return Unauthorized(); var identity = await links.GetUserAsync(userId, ct); if (!identity.Succeeded || identity.Value?.CurrentLink is null) return Unauthorized(); var result = await service.GetAsync(id, ct); if (!result.Succeeded || result.Value?.EmployeeId != identity.Value.CurrentLink.EmployeeId) return NotFound(); return result.ToActionResult();
    }

    [HttpGet("/api/me/variable-pay/{id:guid}/history"), HasPermission(Permissions.Payroll.VariablePayViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<VariablePayAwardHistoryDto>>>> MyHistory(Guid id, CancellationToken ct)
    {
        if (tenant.UserId is not Guid userId) return Unauthorized(); var identity = await links.GetUserAsync(userId, ct); if (!identity.Succeeded || identity.Value?.CurrentLink is null) return Unauthorized(); var award = await service.GetAsync(id, ct); if (!award.Succeeded || award.Value?.EmployeeId != identity.Value.CurrentLink.EmployeeId) return NotFound(); return (await service.GetHistoryAsync(id, ct)).ToActionResult();
    }
}
