using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendanceAdminCorrectionService(
    IHrmsDbContext db,
    ITenantContext tenant,
    IEffectiveEmploymentResolver employment,
    IAttendanceDayProcessor processor,
    IAttendancePeriodLockService periodLock,
    TimeProvider? timeProvider = null,
    IAttendanceAuthorizationService? authorization = null,
    IEmployeeAccessScopeService? accessScope = null,
    IEmployeeManagerResolver? managers = null) : IAttendanceAdminCorrectionService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<Result<AdminAttendanceCorrectionDto>> CreateAsync(AdminAttendanceCorrectionRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty || tenant.UserId is not Guid userId || userId == Guid.Empty) return Result<AdminAttendanceCorrectionDto>.Unauthorized("An authenticated tenant and account are required.");
        if (request.EmployeeId == Guid.Empty) return Result<AdminAttendanceCorrectionDto>.Invalid("employeeId", "Employee is required.");
        if (authorization is not null && !await CanAccessTargetAsync(tenantId, userId, request.EmployeeId, request.BusinessDate, ct))
        {
            return Result<AdminAttendanceCorrectionDto>.NotFound("Attendance correction was not found.");
        }
        if (request.CorrectedInAtUtc is null && request.CorrectedOutAtUtc is null) return Result<AdminAttendanceCorrectionDto>.Invalid("correction", "At least one corrected punch is required.");
        if (request.CorrectedInAtUtc is DateTime input && request.CorrectedOutAtUtc is DateTime output && output < input) return Result<AdminAttendanceCorrectionDto>.Invalid("correctedOutAtUtc", "Corrected out time cannot precede corrected in time.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return Result<AdminAttendanceCorrectionDto>.Invalid("reason", "A reason is required.");
        var reason = request.Reason.Trim();
        if (reason.Length > 2000) return Result<AdminAttendanceCorrectionDto>.Invalid("reason", "Reason cannot exceed 2000 characters.");
        if (!(await periodLock.EnsureDateIsOpenAsync(request.BusinessDate, ct)).Succeeded) return Result<AdminAttendanceCorrectionDto>.Conflict("The Attendance period is closed and must be reopened before this change.");
        var resolved = await employment.ResolveAsync(tenantId, request.EmployeeId, request.BusinessDate, ct);
        if (resolved.Status != EffectiveEmploymentResolutionStatus.Resolved) return Result<AdminAttendanceCorrectionDto>.Invalid("employeeId", resolved.Message);
        if (!await db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == request.EmployeeId, ct)) return Result<AdminAttendanceCorrectionDto>.NotFound("Employee was not found in this tenant.");
        if (await db.LeaveRequestDays.AnyAsync(x => x.TenantId == tenantId && x.Date == request.BusinessDate && x.LeaveRequest != null && x.LeaveRequest.EmployeeId == request.EmployeeId && x.LeaveRequest.Status == HRMS.Domain.Enums.LeaveRequestStatus.Approved, ct)) return Result<AdminAttendanceCorrectionDto>.Conflict("Approved Leave applies to this attendance date.");
        if (await db.AttendanceOnDutyRequests.AnyAsync(x => x.TenantId == tenantId && x.EmployeeId == request.EmployeeId && x.Status == HRMS.Domain.Enums.AttendanceRequestStatus.Approved && x.StartDate <= request.BusinessDate && request.BusinessDate <= x.EndDate, ct)) return Result<AdminAttendanceCorrectionDto>.Conflict("Approved On Duty applies to this attendance date.");

        await using var transaction = await db.BeginTransactionAsync(ct);
        var version = (await db.AttendanceAdminCorrections.Where(x => x.TenantId == tenantId && x.EmployeeId == request.EmployeeId && x.BusinessDate == request.BusinessDate).Select(x => (int?)x.CorrectionVersion).MaxAsync(ct) ?? 0) + 1;
        var correction = new AttendanceAdminCorrection { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = request.EmployeeId, BusinessDate = request.BusinessDate, CorrectedInAtUtc = request.CorrectedInAtUtc, CorrectedOutAtUtc = request.CorrectedOutAtUtc, Reason = reason, CreatedByUserId = userId, CreatedAtUtc = clock.GetUtcNow().UtcDateTime, CorrectionVersion = version };
        db.AttendanceAdminCorrections.Add(correction);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<AdminAttendanceCorrectionDto>.Conflict("Another correction was submitted for this employee and date. Retry the correction.");
        }
        var processed = await processor.ProcessAsync(request.EmployeeId, request.BusinessDate, ct);
        if (!processed.Succeeded) return Result<AdminAttendanceCorrectionDto>.Failure(processed.Status, processed.Message, processed.Errors);
        await transaction.CommitAsync(ct);
        return Result<AdminAttendanceCorrectionDto>.Success(Map(correction), "Attendance correction applied.");
    }

    public async Task<Result<PagedResult<AdminAttendanceCorrectionDto>>> ListAsync(AdminAttendanceCorrectionQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty || tenant.UserId is not Guid userId || userId == Guid.Empty) return Result<PagedResult<AdminAttendanceCorrectionDto>>.Unauthorized("An authenticated tenant and account are required.");
        var q = db.AttendanceAdminCorrections.AsNoTracking().Where(x => x.TenantId == tenantId);
        if (authorization is not null)
        {
            var date = query.FromDate ?? DateOnly.FromDateTime(clock.GetUtcNow().DateTime);
            var scope = await authorization.BuildEmployeePredicateAsync(Permissions.Attendance.AdminCorrectionManage, false, true, true, date, ct);
            if (!scope.Succeeded || scope.Value is null) return Result<PagedResult<AdminAttendanceCorrectionDto>>.Failure(scope.Status, scope.Message, scope.Errors);
            q = q.Where(x => db.Employees.Where(scope.Value).Any(e => e.Id == x.EmployeeId));
        }
        if (query.EmployeeId is Guid employeeId) q = q.Where(x => x.EmployeeId == employeeId);
        if (query.FromDate is DateOnly from) q = q.Where(x => x.BusinessDate >= from);
        if (query.ToDate is DateOnly to) q = q.Where(x => x.BusinessDate <= to);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.BusinessDate).ThenByDescending(x => x.CorrectionVersion).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return Result<PagedResult<AdminAttendanceCorrectionDto>>.Success(new(rows.Select(Map).ToList(), query.Page, query.PageSize, total));
    }

    public async Task<Result<AdminAttendanceCorrectionDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty || tenant.UserId is not Guid userId || userId == Guid.Empty) return Result<AdminAttendanceCorrectionDto>.Unauthorized("An authenticated tenant and account are required.");
        var row = await db.AttendanceAdminCorrections.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (row is null) return Result<AdminAttendanceCorrectionDto>.NotFound("Attendance correction was not found.");
        if (authorization is not null && !await CanAccessTargetAsync(tenantId, userId, row.EmployeeId, row.BusinessDate, ct))
        {
            return Result<AdminAttendanceCorrectionDto>.NotFound("Attendance correction was not found.");
        }
        return Result<AdminAttendanceCorrectionDto>.Success(Map(row));
    }

    private async Task<bool> CanAccessTargetAsync(Guid tenantId, Guid userId, Guid employeeId, DateOnly date, CancellationToken ct)
    {
        var linkedEmployeeId = await db.AccountEmployeeCurrentLinks.AsNoTracking().Where(x => x.TenantId == tenantId && x.UserId == userId).Select(x => (Guid?)x.EmployeeId).SingleOrDefaultAsync(ct);
        if (linkedEmployeeId == employeeId) return true;
        if (accessScope is not null)
        {
            var predicate = await accessScope.BuildRoleScopePredicateAsync(date, ct);
            if (await db.Employees.AsNoTracking().Where(x => x.TenantId == tenantId).Where(predicate).AnyAsync(x => x.Id == employeeId, ct)) return true;
        }
        if (managers is not null && linkedEmployeeId is Guid managerId)
        {
            var resolved = await managers.ResolveAsync(employeeId, date, ct);
            if (resolved.Succeeded && resolved.Value?.Status == EmployeeManagerResolutionStatus.Resolved && resolved.Value.ManagerId == managerId) return true;
        }
        return false;
    }

    private static AdminAttendanceCorrectionDto Map(AttendanceAdminCorrection x) => new(x.Id, x.EmployeeId, x.BusinessDate, x.CorrectedInAtUtc, x.CorrectedOutAtUtc, x.Reason, x.CreatedByUserId, x.CreatedAtUtc, x.CorrectionVersion);
}
