using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendancePayrollSnapshotResolver(IHrmsDbContext db, ITenantContext tenant) : IAttendancePayrollSnapshotResolver
{
    public async Task<Result<PayrollAttendanceSnapshotContract?>> ResolveAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PayrollAttendanceSnapshotContract?>.Unauthorized("No authenticated tenant.");
        var attendancePeriod = await db.AttendancePeriods.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.StartDate == periodStart && x.EndDate == periodEnd, ct);
        // Existing payroll periods created before the Attendance contract remain compatible until an Attendance period exists.
        if (attendancePeriod is null) return Result<PayrollAttendanceSnapshotContract?>.Success(null);
        if (attendancePeriod.Status != Domain.Enums.AttendancePeriodStatus.Closed) return Result<PayrollAttendanceSnapshotContract?>.Conflict("AttendanceNotFinalized: the Attendance period is not finalized.");
        var snapshot = await db.PayrollAttendanceSnapshots.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.AttendancePeriodId == attendancePeriod.Id && x.EmployeeId == employeeId && x.IsCurrent, ct);
        if (snapshot is null) return Result<PayrollAttendanceSnapshotContract?>.Conflict("AttendanceSnapshotMissing: no current payroll Attendance snapshot exists.");
        return Result<PayrollAttendanceSnapshotContract?>.Success(new(snapshot.Id, snapshot.Version, snapshot.EligibleDays, snapshot.PayableDays, snapshot.LopDays, snapshot.PresentDays, snapshot.AbsentDays));
    }
}
