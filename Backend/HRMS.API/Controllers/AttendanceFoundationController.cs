using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Attendance;
using HRMS.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Route("api/attendance"), Produces("application/json"), Authorize]
public sealed class AttendanceFoundationController(IAttendanceFoundationService service) : ControllerBase
{
    [HttpGet("shifts"), HasPermission(Permissions.Attendance.View)] public async Task<ActionResult<ApiResponse<IReadOnlyList<ShiftDto>>>> Shifts([FromQuery] ShiftQuery query, CancellationToken ct) => (await service.GetShiftsAsync(query, ct)).ToActionResult();
    [HttpPost("shifts"), HasPermission(Permissions.Attendance.ShiftManage)] public async Task<ActionResult<ApiResponse<ShiftDto>>> CreateShift(ShiftRequest request, CancellationToken ct) => (await service.CreateShiftAsync(request, ct)).ToCreatedResult(nameof(Shifts), _ => new { });
    [HttpPut("shifts/{id:guid}"), HasPermission(Permissions.Attendance.ShiftManage)] public async Task<ActionResult<ApiResponse<ShiftDto>>> UpdateShift(Guid id, ShiftRequest request, CancellationToken ct) => (await service.UpdateShiftAsync(id, request, ct)).ToActionResult();
    [HttpGet("patterns"), HasPermission(Permissions.Attendance.View)] public async Task<ActionResult<ApiResponse<IReadOnlyList<ShiftPatternDto>>>> Patterns(CancellationToken ct) => (await service.GetPatternsAsync(ct)).ToActionResult();
    [HttpPost("patterns"), HasPermission(Permissions.Attendance.PatternManage)] public async Task<ActionResult<ApiResponse<ShiftPatternDto>>> CreatePattern(ShiftPatternRequest request, CancellationToken ct) => (await service.CreatePatternAsync(request, ct)).ToCreatedResult(nameof(Patterns), _ => new { });
    [HttpPost("applicability"), HasPermission(Permissions.Attendance.PatternManage)] public async Task<ActionResult<ApiResponse<ShiftApplicabilityRequest>>> AddApplicability(ShiftApplicabilityRequest request, CancellationToken ct) => (await service.AddApplicabilityAsync(request, ct)).ToActionResult();
    [HttpGet("employees/{employeeId:guid}/roster/{date}"), HasPermission(Permissions.Attendance.View)] public async Task<ActionResult<ApiResponse<ShiftResolutionDto>>> Resolve(Guid employeeId, DateOnly date, CancellationToken ct) => (await service.ResolveAsync(employeeId, date, ct)).ToActionResult();
    [HttpPost("roster/assign"), HasPermission(Permissions.Attendance.RosterManage)] public async Task<ActionResult<ApiResponse<IReadOnlyList<RosterDayDto>>>> Assign(RosterAssignmentRequest request, CancellationToken ct) => (await service.AssignRosterAsync(request, ct)).ToActionResult();
    [HttpGet("roster/template"), HasPermission(Permissions.Attendance.RosterUpload)] public IActionResult Template() => File("EmployeeCode,Date,ShiftCode,DayType\r\n", "text/csv", "attendance-roster-template.csv");
    [HttpPost("roster/upload/validate"), HasPermission(Permissions.Attendance.RosterUpload), RequestSizeLimit(10_000_000)] public async Task<ActionResult<ApiResponse<RosterUploadBatchDto>>> ValidateUpload(IFormFile file, CancellationToken ct) { if (file is null || file.Length == 0) return Result<RosterUploadBatchDto>.Invalid("file", "A non-empty CSV file is required.").ToActionResult(); await using var stream = file.OpenReadStream(); return (await service.ValidateRosterUploadAsync(file.FileName, stream, ct)).ToActionResult(); }
    [HttpPost("roster/upload/{batchId:guid}/commit"), HasPermission(Permissions.Attendance.RosterUpload)] public async Task<ActionResult<ApiResponse<RosterUploadBatchDto>>> CommitUpload(Guid batchId, CancellationToken ct) => (await service.CommitRosterUploadAsync(batchId, ct)).ToActionResult();
}
