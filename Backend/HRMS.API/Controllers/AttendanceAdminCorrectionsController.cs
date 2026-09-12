using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Authorize, Route("api/attendance/admin-corrections")]
public sealed class AttendanceAdminCorrectionsController(IAttendanceAdminCorrectionService service) : ControllerBase
{
    [HttpPost, HasPermission(Permissions.Attendance.AdminCorrectionManage)]
    public async Task<ActionResult<ApiResponse<AdminAttendanceCorrectionDto>>> Create(AdminAttendanceCorrectionRequest request, CancellationToken ct) => (await service.CreateAsync(request, ct)).ToActionResult();

    [HttpGet, HasPermission(Permissions.Attendance.AdminCorrectionManage)]
    public async Task<ActionResult<ApiResponse<PagedResult<AdminAttendanceCorrectionDto>>>> List([FromQuery] AdminAttendanceCorrectionQuery query, CancellationToken ct) => (await service.ListAsync(query, ct)).ToActionResult();

    [HttpGet("{id:guid}"), HasPermission(Permissions.Attendance.AdminCorrectionManage)]
    public async Task<ActionResult<ApiResponse<AdminAttendanceCorrectionDto>>> Get(Guid id, CancellationToken ct) => (await service.GetAsync(id, ct)).ToActionResult();
}
