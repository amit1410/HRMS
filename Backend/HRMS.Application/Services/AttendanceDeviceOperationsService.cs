using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Text.Json;

namespace HRMS.Application.Services;

public sealed class AttendanceDeviceOperationsService(
    IHrmsDbContext db,
    ITenantContext tenant,
    IAttendanceDeviceIntegrationService ingestion,
    IEnumerable<IAttendancePunchSource> punchSources) : IAttendanceDeviceOperationsService
{
    private readonly IReadOnlyList<IAttendancePunchSource> _sources = punchSources.ToArray();

    public async Task<Result<PagedResult<AttendanceDeviceDto>>> GetDevicesAsync(AttendanceDeviceQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<PagedResult<AttendanceDeviceDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query.Page, query.PageSize)) return Result<PagedResult<AttendanceDeviceDto>>.Invalid("page", "Page size must be between 1 and 200.");
        var rows = db.AttendanceDevices.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (query.Status is not null) rows = rows.Where(x => x.Status == query.Status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            rows = rows.Where(x => x.Code.Contains(term) || x.Name.Contains(term) || (x.Vendor != null && x.Vendor.Contains(term)) || (x.SerialNumber != null && x.SerialNumber.Contains(term)));
        }
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).Select(x => ToDto(x)).ToListAsync(ct);
        return Result<PagedResult<AttendanceDeviceDto>>.Success(new(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<AttendanceDeviceDto>> GetDeviceAsync(Guid id, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<AttendanceDeviceDto>.Unauthorized("No authenticated tenant.");
        var item = await db.AttendanceDevices.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        return item is null ? Result<AttendanceDeviceDto>.NotFound("DeviceNotFound") : Result<AttendanceDeviceDto>.Success(ToDto(item));
    }

    public async Task<Result<AttendanceDeviceDto>> CreateDeviceAsync(AttendanceDeviceRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<AttendanceDeviceDto>.Unauthorized("No authenticated tenant.");
        var validation = await ValidateDeviceAsync(tenantId, request, null, ct);
        if (validation is not null) return Result<AttendanceDeviceDto>.Failure(validation.Value.Status, validation.Value.Message);
        var item = new AttendanceDevice
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Code = request.Code.Trim(), Name = request.Name.Trim(),
            DeviceType = request.DeviceType.Trim(), Vendor = Clean(request.Vendor), SerialNumber = Clean(request.SerialNumber),
            WorkLocationId = request.WorkLocationId, TimeZoneId = request.TimeZoneId.Trim(), ConnectionMode = request.ConnectionMode,
            CredentialReference = Clean(request.CredentialReference), Status = AttendanceDeviceStatus.Active
        };
        db.AttendanceDevices.Add(item);
        AddAudit(tenantId, "DeviceCreated", item.Id, null, null, new { item.Code, item.Name, item.DeviceType, item.Vendor, item.SerialNumber, item.WorkLocationId, item.TimeZoneId, item.ConnectionMode, item.Status, CredentialReferenceConfigured = item.CredentialReference is not null });
        await db.SaveChangesAsync(ct);
        return Result<AttendanceDeviceDto>.Success(ToDto(item), "Device created.");
    }

    public async Task<Result<AttendanceDeviceDto>> UpdateDeviceAsync(Guid id, AttendanceDeviceRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<AttendanceDeviceDto>.Unauthorized("No authenticated tenant.");
        var item = await db.AttendanceDevices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (item is null) return Result<AttendanceDeviceDto>.NotFound("DeviceNotFound");
        var validation = await ValidateDeviceAsync(tenantId, request, id, ct);
        if (validation is not null) return Result<AttendanceDeviceDto>.Failure(validation.Value.Status, validation.Value.Message);
        var before = DeviceAuditSnapshot(item);
        item.Code = request.Code.Trim(); item.Name = request.Name.Trim(); item.DeviceType = request.DeviceType.Trim();
        item.Vendor = Clean(request.Vendor); item.SerialNumber = Clean(request.SerialNumber); item.WorkLocationId = request.WorkLocationId;
        item.TimeZoneId = request.TimeZoneId.Trim(); item.ConnectionMode = request.ConnectionMode;
        if (!string.IsNullOrWhiteSpace(request.CredentialReference)) item.CredentialReference = request.CredentialReference.Trim();
        AddAudit(tenantId, "DeviceUpdated", item.Id, null, null, new { Before = before, After = DeviceAuditSnapshot(item), CredentialReferenceChanged = !string.IsNullOrWhiteSpace(request.CredentialReference) });
        await db.SaveChangesAsync(ct);
        return Result<AttendanceDeviceDto>.Success(ToDto(item), "Device updated.");
    }

    public async Task<Result<AttendanceDeviceDto>> SetDeviceStatusAsync(Guid id, AttendanceDeviceStatus status, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<AttendanceDeviceDto>.Unauthorized("No authenticated tenant.");
        if (!Enum.IsDefined(status)) return Result<AttendanceDeviceDto>.Invalid("status", "Invalid device status.");
        var item = await db.AttendanceDevices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (item is null) return Result<AttendanceDeviceDto>.NotFound("DeviceNotFound");
        if (item.Status == AttendanceDeviceStatus.Disabled && status != AttendanceDeviceStatus.Disabled)
            return Result<AttendanceDeviceDto>.Conflict("Disabled devices cannot be reactivated; create a replacement device.");
        var priorStatus = item.Status;
        item.Status = status;
        if (priorStatus != status)
            AddAudit(tenantId, status == AttendanceDeviceStatus.Active ? "DeviceActivated" : status == AttendanceDeviceStatus.Inactive ? "DeviceDeactivated" : "DeviceDisabled",
                item.Id, null, null, new { BeforeStatus = priorStatus, AfterStatus = status });
        await db.SaveChangesAsync(ct);
        return Result<AttendanceDeviceDto>.Success(ToDto(item), "Device status updated.");
    }

    public async Task<Result<PagedResult<AttendanceDeviceMappingDto>>> GetMappingsAsync(AttendanceDeviceMappingQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<PagedResult<AttendanceDeviceMappingDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query.Page, query.PageSize)) return Result<PagedResult<AttendanceDeviceMappingDto>>.Invalid("page", "Page size must be between 1 and 200.");
        var rows = db.AttendanceDeviceEmployeeMappings.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (query.DeviceId is Guid deviceId) rows = rows.Where(x => x.AttendanceDeviceId == deviceId);
        if (query.EmployeeId is Guid employeeId) rows = rows.Where(x => x.EmployeeId == employeeId);
        if (!string.IsNullOrWhiteSpace(query.ExternalEmployeeIdentifier)) rows = rows.Where(x => x.ExternalEmployeeIdentifier.Contains(query.ExternalEmployeeIdentifier.Trim()));
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderBy(x => x.AttendanceDeviceId).ThenBy(x => x.ExternalEmployeeIdentifier).ThenByDescending(x => x.EffectiveFrom)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Join(db.Employees.AsNoTracking(), m => new { m.TenantId, m.EmployeeId }, e => new { e.TenantId, EmployeeId = e.Id },
                (m, e) => new AttendanceDeviceMappingDto(m.Id, m.AttendanceDeviceId, m.ExternalEmployeeIdentifier, m.EmployeeId, e.EmployeeCode ?? string.Empty, m.EffectiveFrom, m.EffectiveTo, m.Status))
            .ToListAsync(ct);
        return Result<PagedResult<AttendanceDeviceMappingDto>>.Success(new(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<AttendanceDeviceMappingDto>> CreateMappingAsync(AttendanceDeviceMappingRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<AttendanceDeviceMappingDto>.Unauthorized("No authenticated tenant.");
        await using var transaction = await db.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var invalid = await ValidateMappingAsync(tenantId, request, null, ct);
            if (invalid is not null) return Result<AttendanceDeviceMappingDto>.Failure(invalid.Value.Status, invalid.Value.Message);
            var item = new AttendanceDeviceEmployeeMapping { Id = Guid.NewGuid(), TenantId = tenantId, AttendanceDeviceId = request.DeviceId,
                ExternalEmployeeIdentifier = request.ExternalEmployeeIdentifier.Trim(), EmployeeId = request.EmployeeId,
                EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, Status = request.Status };
            db.AttendanceDeviceEmployeeMappings.Add(item);
            AddAudit(tenantId, "MappingCreated", item.AttendanceDeviceId, item.Id, item.EmployeeId,
                new { item.ExternalEmployeeIdentifier, item.EffectiveFrom, item.EffectiveTo, item.Status });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            var employeeCode = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == item.EmployeeId).Select(x => x.EmployeeCode).SingleAsync(ct);
            return Result<AttendanceDeviceMappingDto>.Success(ToDto(item, employeeCode ?? string.Empty), "Mapping created.");
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ClearChangeTracker();
            return Result<AttendanceDeviceMappingDto>.Conflict("MappingConflict: active effective mappings cannot overlap for one device identity.");
        }
    }

    public async Task<Result<AttendanceDeviceMappingDto>> UpdateMappingAsync(Guid id, AttendanceDeviceMappingRequest request, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<AttendanceDeviceMappingDto>.Unauthorized("No authenticated tenant.");
        var item = await db.AttendanceDeviceEmployeeMappings.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (item is null) return Result<AttendanceDeviceMappingDto>.NotFound("MappingNotFound");
        if (item.AttendanceDeviceId != request.DeviceId || item.EmployeeId != request.EmployeeId ||
            !string.Equals(item.ExternalEmployeeIdentifier, request.ExternalEmployeeIdentifier.Trim(), StringComparison.Ordinal))
            return Result<AttendanceDeviceMappingDto>.Conflict("Mapping identity is immutable; deactivate it and create a new effective mapping.");
        var invalid = await ValidateMappingAsync(tenantId, request, id, ct);
        if (invalid is not null) return Result<AttendanceDeviceMappingDto>.Failure(invalid.Value.Status, invalid.Value.Message);
        var before = new { item.ExternalEmployeeIdentifier, item.EffectiveFrom, item.EffectiveTo, item.Status };
        item.EffectiveFrom = request.EffectiveFrom; item.EffectiveTo = request.EffectiveTo; item.Status = request.Status;
        AddAudit(tenantId, "MappingUpdated", item.AttendanceDeviceId, item.Id, item.EmployeeId,
            new { Before = before, After = new { item.ExternalEmployeeIdentifier, item.EffectiveFrom, item.EffectiveTo, item.Status } });
        await db.SaveChangesAsync(ct);
        var employeeCode = await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == item.EmployeeId).Select(x => x.EmployeeCode).SingleAsync(ct);
        return Result<AttendanceDeviceMappingDto>.Success(ToDto(item, employeeCode ?? string.Empty), "Mapping updated.");
    }

    public async Task<Result<bool>> DeactivateMappingAsync(Guid id, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<bool>.Unauthorized("No authenticated tenant.");
        var item = await db.AttendanceDeviceEmployeeMappings.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (item is null) return Result<bool>.NotFound("MappingNotFound");
        if (item.Status == AttendanceDeviceMappingStatus.Inactive) return Result<bool>.Success(true, "Mapping already inactive.");
        item.Status = AttendanceDeviceMappingStatus.Inactive;
        AddAudit(tenantId, "MappingDeactivated", item.AttendanceDeviceId, item.Id, item.EmployeeId,
            new { item.ExternalEmployeeIdentifier, item.EffectiveFrom, item.EffectiveTo, Status = AttendanceDeviceMappingStatus.Inactive });
        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true, "Mapping deactivated.");
    }

    public async Task<Result<PagedResult<AttendanceDeviceIssueDto>>> GetIssuesAsync(AttendanceDeviceIssueQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<PagedResult<AttendanceDeviceIssueDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query.Page, query.PageSize) || query.FromUtc > query.ToUtc) return Result<PagedResult<AttendanceDeviceIssueDto>>.Invalid("query", "Invalid paging or date range.");
        var rows = db.AttendanceDeviceIngestionEvents.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (query.Status is not null) rows = rows.Where(x => x.Status == query.Status);
        if (query.DeviceId is Guid deviceId) rows = rows.Where(x => x.AttendanceDeviceId == deviceId);
        if (query.EmployeeId is Guid employeeId) rows = rows.Where(x => x.EmployeeId == employeeId);
        if (query.SyncRunId is Guid runId) rows = rows.Where(x => x.AttendanceDeviceSyncRunId == runId);
        if (query.FromUtc is DateTime from) rows = rows.Where(x => x.ReceivedAtUtc >= from);
        if (query.ToUtc is DateTime to) rows = rows.Where(x => x.ReceivedAtUtc <= to);
        if (!string.IsNullOrWhiteSpace(query.ExternalEmployeeIdentifier)) rows = rows.Where(x => x.ExternalEmployeeIdentifier.Contains(query.ExternalEmployeeIdentifier.Trim()));
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(x => x.ReceivedAtUtc).ThenBy(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new AttendanceDeviceIssueDto(x.Id, x.AttendanceDeviceId, x.AttendanceDeviceSyncRunId, x.EmployeeId, x.AttendancePunchId,
                x.ExternalEventId, x.ExternalEmployeeIdentifier, x.OccurredAtUtc, x.ReceivedAtUtc, x.Direction, x.Status, x.SanitizedError)).ToListAsync(ct);
        return Result<PagedResult<AttendanceDeviceIssueDto>>.Success(new(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<AttendanceDeviceBatchResult>> ReprocessIssueAsync(Guid id, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<AttendanceDeviceBatchResult>.Unauthorized("No authenticated tenant.");
        var issue = await db.AttendanceDeviceIngestionEvents.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (issue is null) return Result<AttendanceDeviceBatchResult>.NotFound("IssueNotFound");
        if (issue.AttendancePunchId is not null && issue.Status is (AttendanceDeviceIngestionStatus.Accepted or AttendanceDeviceIngestionStatus.Duplicate))
            return Result<AttendanceDeviceBatchResult>.Success(new(
                issue.AttendanceDeviceSyncRunId ?? Guid.Empty, 1, 0, 1, 0, 0,
                [new(issue.ExternalEventId, AttendanceDeviceIngestionStatus.Duplicate, "Authoritative punch already exists.")], null));
        if (issue.Status is not (AttendanceDeviceIngestionStatus.Unmapped or AttendanceDeviceIngestionStatus.RequiresPeriodReopen) || issue.AttendanceDeviceId is not Guid deviceId)
            return Result<AttendanceDeviceBatchResult>.Conflict("IssueNotReprocessable");
        var device = await db.AttendanceDevices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deviceId, ct);
        if (device is null) return Result<AttendanceDeviceBatchResult>.NotFound("DeviceNotFound");
        if (device.Status != AttendanceDeviceStatus.Active) return Result<AttendanceDeviceBatchResult>.Conflict("DeviceInactive");
        var result = await ingestion.IngestAsync(new(deviceId, "issue-reprocess",
            [new(issue.ExternalEventId, issue.ExternalEmployeeIdentifier, new DateTimeOffset(DateTime.SpecifyKind(issue.OccurredAtUtc, DateTimeKind.Utc)), issue.Direction)], null), ct);
        if (!result.Succeeded) return result;
        var item = result.Value!.Items.Single();
        issue.Status = item.Status;
        issue.AttendancePunchId = await db.AttendancePunches.AsNoTracking().Where(x => x.TenantId == tenantId && x.Source == PunchSource.Biometric && x.ExternalPunchId == $"{deviceId:N}:{issue.ExternalEventId}").Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (issue.AttendancePunchId is Guid punchId)
            issue.EmployeeId = await db.AttendancePunches.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == punchId).Select(x => (Guid?)x.EmployeeId).SingleOrDefaultAsync(ct);
        issue.SanitizedError = item.Message;
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<Result<PagedResult<AttendanceDeviceSyncRunDto>>> GetSyncRunsAsync(AttendanceDeviceSyncRunQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<PagedResult<AttendanceDeviceSyncRunDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query.Page, query.PageSize) || query.FromUtc > query.ToUtc) return Result<PagedResult<AttendanceDeviceSyncRunDto>>.Invalid("query", "Invalid paging or date range.");
        var rows = db.AttendanceDeviceSyncRuns.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (query.DeviceId is Guid deviceId) rows = rows.Where(x => x.AttendanceDeviceId == deviceId);
        if (query.Status is not null) rows = rows.Where(x => x.Status == query.Status);
        if (query.FromUtc is DateTime from) rows = rows.Where(x => x.StartedAtUtc >= from);
        if (query.ToUtc is DateTime to) rows = rows.Where(x => x.StartedAtUtc <= to);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(x => x.StartedAtUtc).ThenBy(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new AttendanceDeviceSyncRunDto(x.Id, x.AttendanceDeviceId, x.Source, x.Status, x.StartedAtUtc, x.CompletedAtUtc,
                x.ReceivedCount, x.AcceptedCount, x.DuplicateCount, x.RejectedCount, x.UnmappedCount, x.ErrorCount, x.CheckpointBefore, x.CheckpointAfter)).ToListAsync(ct);
        return Result<PagedResult<AttendanceDeviceSyncRunDto>>.Success(new(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<PagedResult<AttendanceDeviceAuditDto>>> GetAuditHistoryAsync(AttendanceDeviceAuditQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<PagedResult<AttendanceDeviceAuditDto>>.Unauthorized("No authenticated tenant.");
        if (!ValidPage(query.Page, query.PageSize)) return Result<PagedResult<AttendanceDeviceAuditDto>>.Invalid("page", "Page size must be between 1 and 200.");
        var rows = db.AttendanceDeviceAuditEvents.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (query.DeviceId is Guid deviceId) rows = rows.Where(x => x.AttendanceDeviceId == deviceId);
        if (query.MappingId is Guid mappingId) rows = rows.Where(x => x.AttendanceDeviceEmployeeMappingId == mappingId);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(x => x.OccurredAtUtc).ThenBy(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new AttendanceDeviceAuditDto(x.Id, x.ActorUserId, x.AttendanceDeviceId, x.AttendanceDeviceEmployeeMappingId, x.EmployeeId, x.Action, x.OccurredAtUtc, x.ContextJson)).ToListAsync(ct);
        return Result<PagedResult<AttendanceDeviceAuditDto>>.Success(new(items, query.Page, query.PageSize, total));
    }

    public async Task<Result<AttendanceDeviceBatchResult>> SyncNowAsync(Guid deviceId, CancellationToken ct = default)
    {
        if (!TryTenant(out var tenantId)) return Result<AttendanceDeviceBatchResult>.Unauthorized("No authenticated tenant.");
        var device = await db.AttendanceDevices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == deviceId, ct);
        if (device is null) return Result<AttendanceDeviceBatchResult>.NotFound("DeviceNotFound");
        if (device.Status != AttendanceDeviceStatus.Active) return Result<AttendanceDeviceBatchResult>.Conflict("DeviceInactive");
        if (device.ConnectionMode != AttendanceDeviceConnectionMode.Pull) return Result<AttendanceDeviceBatchResult>.Conflict("Device does not support pull synchronization.");
        var providerKey = string.IsNullOrWhiteSpace(device.Vendor) ? device.DeviceType : device.Vendor;
        var sources = _sources.Where(x => string.Equals(x.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (sources.Length == 0) return Result<AttendanceDeviceBatchResult>.Conflict($"UnsupportedProvider: no punch source is registered for '{providerKey}'.");
        if (sources.Length > 1) return Result<AttendanceDeviceBatchResult>.Conflict($"UnsupportedProvider: provider key '{providerKey}' is ambiguously registered.");
        var source = sources[0];
        var page = await source.FetchAsync(device.LastSuccessfulCheckpoint, 1000, ct);
        return await ingestion.IngestAsync(new(device.Id, source.ProviderKey, page.Punches, page.NextCheckpoint), ct);
    }

    private async Task<(ResultStatus Status, string Message)?> ValidateDeviceAsync(Guid tenantId, AttendanceDeviceRequest request, Guid? exceptId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 80 || string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 160 || string.IsNullOrWhiteSpace(request.DeviceType) || request.DeviceType.Trim().Length > 80 || request.TimeZoneId is null || !Enum.IsDefined(request.ConnectionMode))
            return (ResultStatus.ValidationFailed, "Invalid device configuration.");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId.Trim()); }
        catch (TimeZoneNotFoundException) { return (ResultStatus.ValidationFailed, "Unknown device time zone."); }
        catch (InvalidTimeZoneException) { return (ResultStatus.ValidationFailed, "Invalid device time zone."); }
        if (await db.AttendanceDevices.AnyAsync(x => x.TenantId == tenantId && x.Code == request.Code.Trim() && x.Id != exceptId, ct))
            return (ResultStatus.Conflict, "DuplicateDeviceCode");
        if (request.WorkLocationId is Guid locationId && !await db.WorkLocations.AnyAsync(x => x.TenantId == tenantId && x.Id == locationId, ct))
            return (ResultStatus.NotFound, "CrossTenantReference: work location was not found in this tenant.");
        return null;
    }

    private async Task<(ResultStatus Status, string Message)?> ValidateMappingAsync(Guid tenantId, AttendanceDeviceMappingRequest request, Guid? exceptId, CancellationToken ct)
    {
        if (request.DeviceId == Guid.Empty || request.EmployeeId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExternalEmployeeIdentifier) || request.ExternalEmployeeIdentifier.Trim().Length > 200 || request.EffectiveTo < request.EffectiveFrom || !Enum.IsDefined(request.Status))
            return (ResultStatus.ValidationFailed, "Invalid employee-device mapping.");
        if (!await db.AttendanceDevices.AnyAsync(x => x.TenantId == tenantId && x.Id == request.DeviceId, ct)) return (ResultStatus.NotFound, "CrossTenantReference: device was not found in this tenant.");
        if (!await db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId, ct)) return (ResultStatus.NotFound, "EmployeeNotFound: employee was not found in this tenant.");
        if (request.Status == AttendanceDeviceMappingStatus.Active)
        {
            var end = request.EffectiveTo ?? DateOnly.MaxValue;
            var overlap = await db.AttendanceDeviceEmployeeMappings.AnyAsync(x => x.TenantId == tenantId && x.Id != exceptId && x.AttendanceDeviceId == request.DeviceId && x.ExternalEmployeeIdentifier == request.ExternalEmployeeIdentifier.Trim() && x.Status == AttendanceDeviceMappingStatus.Active && x.EffectiveFrom <= end && (x.EffectiveTo == null || x.EffectiveTo >= request.EffectiveFrom), ct);
            if (overlap) return (ResultStatus.Conflict, "MappingConflict: active effective mappings cannot overlap for one device identity.");
        }
        return null;
    }

    private bool TryTenant(out Guid tenantId) { tenantId = tenant.TenantId ?? Guid.Empty; return tenantId != Guid.Empty; }
    private void AddAudit(Guid tenantId, string action, Guid? deviceId, Guid? mappingId, Guid? employeeId, object context) =>
        db.AttendanceDeviceAuditEvents.Add(new AttendanceDeviceAuditEvent
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ActorUserId = tenant.UserId, AttendanceDeviceId = deviceId,
            AttendanceDeviceEmployeeMappingId = mappingId, EmployeeId = employeeId, Action = action,
            OccurredAtUtc = DateTime.UtcNow, ContextJson = JsonSerializer.Serialize(context)
        });
    private static object DeviceAuditSnapshot(AttendanceDevice item) => new
    {
        item.Code, item.Name, item.DeviceType, item.Vendor, item.SerialNumber, item.WorkLocationId,
        item.TimeZoneId, item.ConnectionMode, item.Status, CredentialReferenceConfigured = item.CredentialReference is not null
    };
    private static bool ValidPage(int page, int size) => page > 0 && size is > 0 and <= 200;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AttendanceDeviceDto ToDto(AttendanceDevice x) => new(x.Id, x.Code, x.Name, x.DeviceType, x.Vendor, x.SerialNumber, x.WorkLocationId, x.TimeZoneId, x.ConnectionMode, x.Status, x.LastSuccessfulSyncAtUtc, x.LastAttemptedSyncAtUtc);
    private static AttendanceDeviceMappingDto ToDto(AttendanceDeviceEmployeeMapping x, string code) => new(x.Id, x.AttendanceDeviceId, x.ExternalEmployeeIdentifier, x.EmployeeId, code, x.EffectiveFrom, x.EffectiveTo, x.Status);
}
