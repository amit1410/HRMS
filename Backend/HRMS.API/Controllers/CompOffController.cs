using HRMS.API.Extensions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.API.Security;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController]
[Route("api/attendance/comp-off")]
[Authorize]
public sealed class CompOffController(ICompOffService service, IEmployeeIdentityResolver identityResolver) : ControllerBase
{
    [HttpGet("balance")]
    [HasPermission(Permissions.Attendance.CompOffViewSelf)]
    public async Task<ActionResult<ApiResponse<CompOffBalanceDto>>> Balance(CancellationToken ct) =>
        (await CurrentEmployee(ct) is { } employee ? await service.GetBalanceAsync(employee, ct) : Result<CompOffBalanceDto>.Unauthorized("An employee identity is required.")).ToActionResult();

    [HttpGet("earnings")]
    [HasPermission(Permissions.Attendance.CompOffViewSelf)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CompOffEarningDto>>>> Earnings(CancellationToken ct) =>
        (await CurrentEmployee(ct) is { } employee ? await service.GetEarningsAsync(employee, ct) : Result<IReadOnlyList<CompOffEarningDto>>.Unauthorized("An employee identity is required.")).ToActionResult();

    [HttpGet("ledger")]
    [HasPermission(Permissions.Attendance.CompOffViewSelf)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CompOffLedgerDto>>>> Ledger(CancellationToken ct) =>
        (await CurrentEmployee(ct) is { } employee ? await service.GetLedgerAsync(employee, ct) : Result<IReadOnlyList<CompOffLedgerDto>>.Unauthorized("An employee identity is required.")).ToActionResult();

    [HttpGet("operations")]
    public async Task<ActionResult<ApiResponse<PagedResult<CompOffOperationalEarningDto>>>> Operations([FromQuery] CompOffOperationalQuery query, CancellationToken ct) =>
        (await service.GetOperationalAsync(query, ct)).ToActionResult();

    [HttpPost("policies")]
    [HasPermission(Permissions.Attendance.CompOffManage)]
    public async Task<ActionResult<ApiResponse<CompOffPolicyDto>>> CreatePolicy(CompOffPolicyRequest request, CancellationToken ct) => (await service.CreatePolicyAsync(request, ct)).ToActionResult();

    [HttpPost("earnings")]
    [HasPermission(Permissions.Attendance.CompOffManage)]
    public async Task<ActionResult<ApiResponse<CompOffEarningDto>>> Earn(CompOffEarnRequest request, CancellationToken ct) => (await service.EarnAsync(request, ct)).ToActionResult();

    [HttpPost("earnings/{earningId:guid}/approve")]
    [HasPermission(Permissions.Attendance.CompOffApprove)]
    public async Task<ActionResult<ApiResponse<CompOffEarningDto>>> Approve(Guid earningId, CancellationToken ct) => (await service.ApproveAsync(earningId, ct)).ToActionResult();

    [HttpPost("earnings/{earningId:guid}/reject")]
    [HasPermission(Permissions.Attendance.CompOffApprove)]
    public async Task<ActionResult<ApiResponse<CompOffEarningDto>>> Reject(Guid earningId, CancellationToken ct) => (await service.RejectAsync(earningId, ct)).ToActionResult();

    [HttpPost("leave/{leaveRequestId:guid}/reserve")]
    [HasPermission(Permissions.Attendance.CompOffManage)]
    public async Task<ActionResult<ApiResponse<bool>>> Reserve(Guid leaveRequestId, Guid employeeId, int minutes, CancellationToken ct) => (await service.ReserveAsync(leaveRequestId, employeeId, minutes, ct)).ToActionResult();

    [HttpPost("leave/{leaveRequestId:guid}/consume")]
    [HasPermission(Permissions.Attendance.CompOffManage)]
    public async Task<ActionResult<ApiResponse<bool>>> Consume(Guid leaveRequestId, CancellationToken ct) => (await service.ConsumeAsync(leaveRequestId, ct)).ToActionResult();

    [HttpPost("leave/{leaveRequestId:guid}/release")]
    [HasPermission(Permissions.Attendance.CompOffManage)]
    public async Task<ActionResult<ApiResponse<bool>>> Release(Guid leaveRequestId, CancellationToken ct) => (await service.ReleaseAsync(leaveRequestId, ct)).ToActionResult();

    [HttpPost("leave/{leaveRequestId:guid}/restore")]
    [HasPermission(Permissions.Attendance.CompOffManage)]
    public async Task<ActionResult<ApiResponse<bool>>> Restore(Guid leaveRequestId, CancellationToken ct) => (await service.RestoreAsync(leaveRequestId, ct)).ToActionResult();

    [HttpPost("expiry")]
    [HasPermission(Permissions.Attendance.CompOffManage)]
    public async Task<ActionResult<ApiResponse<int>>> Expire(DateOnly asOfDate, CancellationToken ct) => (await service.ExpireAsync(asOfDate, ct)).ToActionResult();

    private async Task<Guid?> CurrentEmployee(CancellationToken ct)
    {
        var result = await identityResolver.ResolveCurrentAsync(ct);
        return result.Succeeded ? result.Value?.EmployeeId : null;
    }
}
