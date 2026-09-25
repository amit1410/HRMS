using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Authorize, Route("api/attendance/overtime")]
public sealed class OvertimeController(IOvertimeService service) : ControllerBase
{
    [HttpPost("policies"), HasPermission(Permissions.Attendance.OvertimeFinalize)]
    public async Task<ActionResult<ApiResponse<OvertimePolicyDto>>> CreatePolicy(OvertimePolicyRequest request, CancellationToken ct) => (await service.CreatePolicyAsync(request, ct)).ToActionResult();

    [HttpPost("requests"), HasPermission(Permissions.Attendance.OvertimeRequest)]
    public async Task<ActionResult<ApiResponse<OvertimeRequestDto>>> CreateRequest(OvertimeRequestRequest request, CancellationToken ct) => (await service.CreateRequestAsync(request, ct)).ToActionResult();

    [HttpPost("requests/{id:guid}/submit"), HasPermission(Permissions.Attendance.OvertimeRequest)]
    public async Task<ActionResult<ApiResponse<OvertimeRequestDto>>> Submit(Guid id, CancellationToken ct) => (await service.SubmitAsync(id, ct)).ToActionResult();

    [HttpPost("requests/{id:guid}/approve"), HasPermission(Permissions.Attendance.OvertimeApprove)]
    public async Task<ActionResult<ApiResponse<OvertimeRequestDto>>> Approve(Guid id, OvertimeDecisionRequest request, CancellationToken ct) => (await service.ApproveAsync(id, request, ct)).ToActionResult();

    [HttpPost("requests/{id:guid}/reject"), HasPermission(Permissions.Attendance.OvertimeApprove)]
    public async Task<ActionResult<ApiResponse<OvertimeRequestDto>>> Reject(Guid id, OvertimeDecisionRequest request, CancellationToken ct) => (await service.RejectAsync(id, request, ct)).ToActionResult();

    [HttpPost("requests/{id:guid}/correct"), HasPermission(Permissions.Attendance.OvertimeApprove)]
    public async Task<ActionResult<ApiResponse<OvertimeRequestDto>>> Correct(Guid id, OvertimeCorrectionRequest request, CancellationToken ct) => (await service.CorrectAsync(id, request, ct)).ToActionResult();

    [HttpPost("periods/{attendancePeriodId:guid}/finalize"), HasPermission(Permissions.Attendance.OvertimeFinalize)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PayrollOvertimeSnapshotContract>>>> Finalize(Guid attendancePeriodId, CancellationToken ct) => (await service.FinalizeAsync(attendancePeriodId, ct)).ToActionResult();

    [HttpPost("periods/{attendancePeriodId:guid}/reopen"), HasPermission(Permissions.Attendance.OvertimeReopen)]
    public async Task<ActionResult<ApiResponse<bool>>> Reopen(Guid attendancePeriodId, OvertimeReopenRequest request, CancellationToken ct) => (await service.ReopenAsync(attendancePeriodId, request.Reason, ct)).ToActionResult();

    [HttpGet("snapshot"), HasPermission(Permissions.Attendance.OvertimeViewAll)]
    public async Task<ActionResult<ApiResponse<PayrollOvertimeSnapshotContract?>>> Snapshot(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct) => (await service.ResolveAsync(employeeId, periodStart, periodEnd, ct)).ToActionResult();
}
