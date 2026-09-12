using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendancePunchIngestionService(
    IHrmsDbContext db,
    ITenantContext tenant,
    IAttendanceFoundationService roster,
    IAttendanceBusinessDateResolver businessDateResolver,
    IAttendanceBusinessTimeZoneProvider? businessTimeZoneProvider = null,
    TimeProvider? timeProvider = null) : IAttendancePunchIngestionService
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly IAttendanceBusinessTimeZoneProvider _timeZoneProvider = businessTimeZoneProvider ?? new AttendanceBusinessTimeZoneProvider();

    public async Task<Result<AttendancePunchDto>> IngestAsync(AttendancePunchIngestionRequest request, CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            return Result<AttendancePunchDto>.Unauthorized("No authenticated tenant.");
        if (request.EmployeeId == Guid.Empty) return Result<AttendancePunchDto>.Invalid("employeeId", "EmployeeId is required.");
        if (!await db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId, cancellationToken))
            return Result<AttendancePunchDto>.NotFound("Employee was not found in this tenant.");
        if (request.ExternalPunchId is { Length: > 200 }) return Result<AttendancePunchDto>.Invalid("externalPunchId", "External punch id cannot exceed 200 characters.");

        var candidate = request.BusinessDate ?? DateOnly.FromDateTime(request.PunchAtUtc.ToUniversalTime());
        var resolution = await roster.ResolveAsync(request.EmployeeId, candidate, cancellationToken);
        if (!resolution.Succeeded) return Result<AttendancePunchDto>.Failure(resolution.Status, resolution.Message, resolution.Errors);
        var shift = resolution.Value?.ShiftId is Guid shiftId
            ? await db.Shifts.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == shiftId && x.IsActive, cancellationToken)
            : null;

        if (shift is null && request.BusinessDate is null && candidate > DateOnly.MinValue)
        {
            var previous = candidate.AddDays(-1);
            var overnightResolution = await roster.ResolveAsync(request.EmployeeId, previous, cancellationToken);
            if (overnightResolution.Succeeded && overnightResolution.Value?.ShiftId is Guid previousShiftId)
            {
                var previousShift = await db.Shifts.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == previousShiftId && x.IsActive, cancellationToken);
                if (previousShift is not null && previousShift.CrossesMidnight) { candidate = previous; shift = previousShift; resolution = overnightResolution; }
            }
        }

        if (shift is null)
        {
            var message = resolution.Value?.Message.Contains("Holiday", StringComparison.OrdinalIgnoreCase) == true
                ? "HolidayWithoutOverride: no effective working Shift applies."
                : resolution.Value?.Message.Contains("WeeklyOff", StringComparison.OrdinalIgnoreCase) == true
                    ? "WeeklyOffWithoutOverride: no effective working Shift applies."
                    : "NoEffectiveShift: no effective working Shift applies.";
            return Result<AttendancePunchDto>.Conflict(message);
        }

        var sourceBit = request.Source switch { PunchSource.Biometric => AttendanceSource.Biometric, PunchSource.Portal => AttendanceSource.Portal, PunchSource.AutoLoginLogout => AttendanceSource.AutoLoginLogout, PunchSource.Manual => AttendanceSource.Manual, _ => (AttendanceSource)0 };
        if ((shift.AllowedAttendanceSources & sourceBit) != sourceBit)
            return Result<AttendancePunchDto>.Forbidden($"Punch source {request.Source} is not enabled for the effective Shift.");
        var businessDate = request.BusinessDate ?? businessDateResolver.Resolve(request.PunchAtUtc, candidate, shift.StartTime, shift.EndTime, shift.CrossesMidnight, shift.MaximumPostShiftMinutes, _timeZoneProvider.GetTimeZone(tenantId));

        if (!string.IsNullOrWhiteSpace(request.ExternalPunchId))
        {
            var existing = await db.AttendancePunches.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Source == request.Source && x.ExternalPunchId == request.ExternalPunchId, cancellationToken);
            if (existing is not null) return Result<AttendancePunchDto>.Success(ToDto(existing), "Duplicate punch ignored; existing punch returned.");
        }

        var item = new AttendancePunch { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = request.EmployeeId, PunchAtUtc = request.PunchAtUtc.ToUniversalTime(), BusinessDate = businessDate, Direction = request.Direction, Source = request.Source, ExternalPunchId = string.IsNullOrWhiteSpace(request.ExternalPunchId) ? null : request.ExternalPunchId.Trim(), DeviceId = request.DeviceId?.Trim(), CapturedAtUtc = _clock.GetUtcNow().UtcDateTime, RawReference = request.RawReference };
        db.AttendancePunches.Add(item);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(item.ExternalPunchId))
        {
            var existing = await db.AttendancePunches.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Source == item.Source && x.ExternalPunchId == item.ExternalPunchId, cancellationToken);
            if (existing is not null) return Result<AttendancePunchDto>.Success(ToDto(existing), "Duplicate punch ignored; existing punch returned.");
            throw;
        }
        return Result<AttendancePunchDto>.Success(ToDto(item), "Attendance punch ingested.");
    }

    private static AttendancePunchDto ToDto(AttendancePunch x) => new(x.Id, x.EmployeeId, x.PunchAtUtc, x.BusinessDate, x.Direction, x.Source, x.ExternalPunchId, x.DeviceId, x.CapturedAtUtc);
}
