using HRMS.API.Extensions;
using HRMS.API.Security;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HRMS.API.Controllers;

[ApiController, Authorize, Route("api/attendance/devices")]
public sealed class AttendanceDevicesController(IAttendanceDeviceOperationsService operations, IAttendanceDeviceIntegrationService ingestion) : ControllerBase
{
    [HttpGet, HasPermission(Permissions.Attendance.DeviceView)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceDeviceDto>>>> List([FromQuery] AttendanceDeviceQuery query, CancellationToken ct) => (await operations.GetDevicesAsync(query, ct)).ToActionResult();

    [HttpGet("{id:guid}"), HasPermission(Permissions.Attendance.DeviceView)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceDto>>> Get(Guid id, CancellationToken ct) => (await operations.GetDeviceAsync(id, ct)).ToActionResult();

    [HttpPost, HasPermission(Permissions.Attendance.DeviceManage)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceDto>>> Create([FromBody] AttendanceDeviceRequest request, CancellationToken ct) => (await operations.CreateDeviceAsync(request, ct)).ToActionResult();

    [HttpPut("{id:guid}"), HasPermission(Permissions.Attendance.DeviceManage)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceDto>>> Update(Guid id, [FromBody] AttendanceDeviceRequest request, CancellationToken ct) => (await operations.UpdateDeviceAsync(id, request, ct)).ToActionResult();

    [HttpPost("{id:guid}/activate"), HasPermission(Permissions.Attendance.DeviceManage)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceDto>>> Activate(Guid id, CancellationToken ct) => (await operations.SetDeviceStatusAsync(id, AttendanceDeviceStatus.Active, ct)).ToActionResult();

    [HttpPost("{id:guid}/deactivate"), HasPermission(Permissions.Attendance.DeviceManage)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceDto>>> Deactivate(Guid id, CancellationToken ct) => (await operations.SetDeviceStatusAsync(id, AttendanceDeviceStatus.Inactive, ct)).ToActionResult();

    [HttpPost("{id:guid}/disable"), HasPermission(Permissions.Attendance.DeviceManage)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceDto>>> Disable(Guid id, CancellationToken ct) => (await operations.SetDeviceStatusAsync(id, AttendanceDeviceStatus.Disabled, ct)).ToActionResult();

    [HttpGet("mappings"), HasPermission(Permissions.Attendance.DeviceView)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceDeviceMappingDto>>>> Mappings([FromQuery] AttendanceDeviceMappingQuery query, CancellationToken ct) => (await operations.GetMappingsAsync(query, ct)).ToActionResult();

    [HttpPost("mappings"), HasPermission(Permissions.Attendance.DeviceMapping)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceMappingDto>>> CreateMapping([FromBody] AttendanceDeviceMappingRequest request, CancellationToken ct) => (await operations.CreateMappingAsync(request, ct)).ToActionResult();

    [HttpPut("mappings/{id:guid}"), HasPermission(Permissions.Attendance.DeviceMapping)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceMappingDto>>> UpdateMapping(Guid id, [FromBody] AttendanceDeviceMappingRequest request, CancellationToken ct) => (await operations.UpdateMappingAsync(id, request, ct)).ToActionResult();

    [HttpPost("mappings/{id:guid}/deactivate"), HasPermission(Permissions.Attendance.DeviceMapping)]
    public async Task<ActionResult<ApiResponse<bool>>> DeactivateMapping(Guid id, CancellationToken ct) => (await operations.DeactivateMappingAsync(id, ct)).ToActionResult();

    [HttpPost("{id:guid}/sync"), HasPermission(Permissions.Attendance.DeviceSync)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceBatchResult>>> Sync(Guid id, CancellationToken ct) => (await operations.SyncNowAsync(id, ct)).ToActionResult();

    [HttpPost("{id:guid}/import"), HasPermission(Permissions.Attendance.DeviceImport)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceBatchResult>>> Import(Guid id, [FromBody] AttendanceDeviceImportRequest request, CancellationToken ct) =>
        (await ingestion.IngestAsync(new(id, "manual-import", request.Punches, request.Checkpoint), ct)).ToActionResult();

    [HttpGet("sync-runs"), HasPermission(Permissions.Attendance.DeviceViewHistory)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceDeviceSyncRunDto>>>> SyncRuns([FromQuery] AttendanceDeviceSyncRunQuery query, CancellationToken ct) => (await operations.GetSyncRunsAsync(query, ct)).ToActionResult();

    [HttpGet("history"), HasPermission(Permissions.Attendance.DeviceViewHistory)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceDeviceAuditDto>>>> History([FromQuery] AttendanceDeviceAuditQuery query, CancellationToken ct) => (await operations.GetAuditHistoryAsync(query, ct)).ToActionResult();

    [HttpGet("issues"), HasPermission(Permissions.Attendance.DeviceView)]
    public async Task<ActionResult<ApiResponse<PagedResult<AttendanceDeviceIssueDto>>>> Issues([FromQuery] AttendanceDeviceIssueQuery query, CancellationToken ct) => (await operations.GetIssuesAsync(query, ct)).ToActionResult();

    [HttpPost("issues/{id:guid}/reprocess"), HasPermission(Permissions.Attendance.DeviceSync)]
    public async Task<ActionResult<ApiResponse<AttendanceDeviceBatchResult>>> Reprocess(Guid id, CancellationToken ct) => (await operations.ReprocessIssueAsync(id, ct)).ToActionResult();
}

public sealed record AttendanceDeviceImportRequest(IReadOnlyList<NormalizedAttendancePunch> Punches, string? Checkpoint = null);
