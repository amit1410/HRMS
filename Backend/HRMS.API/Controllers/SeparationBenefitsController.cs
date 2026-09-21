using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/payroll")]
public sealed class SeparationBenefitsController(ISeparationBenefitsService service, IAccountEmployeeLinkService links, ITenantContext tenant) : ControllerBase
{
    [HttpGet("gratuity-policies"), HasPermission(Permissions.Payroll.SeparationBenefitsView)] public async Task<ActionResult<ApiResponse<IReadOnlyList<GratuityPolicyDto>>>> Policies(CancellationToken ct) => (await service.GetPoliciesAsync(ct)).ToActionResult();
    [HttpPost("gratuity-policies"), HasPermission(Permissions.Payroll.SeparationBenefitsManagePolicies)] public async Task<ActionResult<ApiResponse<GratuityPolicyDto>>> CreatePolicy(GratuityPolicyRequest request, CancellationToken ct) => (await service.CreatePolicyAsync(request, ct)).ToActionResult();
    [HttpPut("gratuity-policies/{id:guid}"), HasPermission(Permissions.Payroll.SeparationBenefitsManagePolicies)] public async Task<ActionResult<ApiResponse<GratuityPolicyDto>>> UpdatePolicy(Guid id, GratuityPolicyRequest request, CancellationToken ct) => (await service.UpdatePolicyAsync(id, request, ct)).ToActionResult();
    [HttpPost("gratuity-policies/{id:guid}/versions"), HasPermission(Permissions.Payroll.SeparationBenefitsManagePolicies)] public async Task<ActionResult<ApiResponse<GratuityPolicyDto>>> Version(Guid id, GratuityPolicyVersionRequest request, CancellationToken ct) => (await service.AddVersionAsync(id, request, ct)).ToActionResult();
    [HttpGet("gratuity-policies/{id:guid}/history"), HasPermission(Permissions.Payroll.SeparationBenefitsViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationBenefitHistoryDto>>>> PolicyHistory(Guid id, CancellationToken ct) => (await service.GetPolicyHistoryAsync(id, ct)).ToActionResult();
    [HttpPost("separation-benefits/{employeeId:guid}/preview"), HasPermission(Permissions.Payroll.SeparationBenefitsCalculate)] public async Task<ActionResult<ApiResponse<SeparationBenefitDto>>> Preview(Guid employeeId, SeparationBenefitCalculationRequest request, CancellationToken ct) => (await service.PreviewAsync(employeeId, request, ct)).ToActionResult();
    [HttpPost("separation-benefits/{employeeId:guid}/calculate"), HasPermission(Permissions.Payroll.SeparationBenefitsCalculate)] public async Task<ActionResult<ApiResponse<SeparationBenefitDto>>> Calculate(Guid employeeId, SeparationBenefitCalculationRequest request, CancellationToken ct)
    {
        if (request.FinalSettlementId is not Guid settlementId) return BadRequest("FinalSettlementId is required for a persisted calculation.");
        return (await service.CalculateAsync(employeeId, settlementId, request, ct)).ToActionResult();
    }
    [HttpGet("separation-benefits/register"), HasPermission(Permissions.Payroll.SeparationBenefitsView)] public async Task<ActionResult<ApiResponse<PagedResult<SeparationBenefitRegisterRowDto>>>> Register([FromQuery] SeparationBenefitQuery query, CancellationToken ct) => (await service.RegisterAsync(query, ct)).ToActionResult();
    [HttpPost("separation-benefits/gratuity/{id:guid}/override"), HasPermission(Permissions.Payroll.SeparationBenefitsOverride)] public async Task<ActionResult<ApiResponse<GratuityCalculationDto>>> Override(Guid id, GratuityOverrideRequest request, CancellationToken ct) => (await service.OverrideAsync(id, request, ct)).ToActionResult();
    [HttpPost("separation-benefits/gratuity/{id:guid}/approve-override"), HasPermission(Permissions.Payroll.SeparationBenefitsApprove)] public async Task<ActionResult<ApiResponse<GratuityCalculationDto>>> ApproveOverride(Guid id, CancellationToken ct) => (await service.ApproveOverrideAsync(id, ct)).ToActionResult();
    [HttpGet("separation-benefits/{employeeId:guid}"), HasPermission(Permissions.Payroll.SeparationBenefitsView)] public async Task<ActionResult<ApiResponse<SeparationBenefitDto>>> Get(Guid employeeId, [FromQuery] Guid? finalSettlementId, CancellationToken ct) => (await service.GetAsync(employeeId, finalSettlementId, ct)).ToActionResult();
    [HttpGet("separation-benefits/{employeeId:guid}/history"), HasPermission(Permissions.Payroll.SeparationBenefitsViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationBenefitHistoryDto>>>> History(Guid employeeId, [FromQuery] Guid? finalSettlementId, CancellationToken ct) => (await service.GetHistoryAsync(employeeId, finalSettlementId, ct)).ToActionResult();

    [HttpGet("/api/me/separation-benefits"), HasPermission(Permissions.Payroll.SeparationBenefitsView)] public async Task<ActionResult<ApiResponse<SeparationBenefitDto>>> My(CancellationToken ct)
    {
        if (tenant.UserId is not Guid userId) return Unauthorized();
        var identity = await links.GetUserAsync(userId, ct); if (!identity.Succeeded || identity.Value?.CurrentLink is null) return Unauthorized(); return (await service.GetAsync(identity.Value.CurrentLink.EmployeeId, null, ct)).ToActionResult();
    }

    [HttpGet("/api/me/separation-benefits/history"), HasPermission(Permissions.Payroll.SeparationBenefitsViewHistory)] public async Task<ActionResult<ApiResponse<IReadOnlyList<SeparationBenefitHistoryDto>>>> MyHistory(CancellationToken ct)
    {
        if (tenant.UserId is not Guid userId) return Unauthorized();
        var identity = await links.GetUserAsync(userId, ct); if (!identity.Succeeded || identity.Value?.CurrentLink is null) return Unauthorized(); return (await service.GetHistoryAsync(identity.Value.CurrentLink.EmployeeId, null, ct)).ToActionResult();
    }
}
