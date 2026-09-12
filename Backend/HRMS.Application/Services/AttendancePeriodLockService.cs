using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendancePeriodLockService(IHrmsDbContext db, ITenantContext tenant) : IAttendancePeriodLockService
{
    public Task<Result<bool>> EnsureDateIsOpenAsync(DateOnly date, CancellationToken ct = default) => EnsureRangeIsOpenAsync(date, date, ct);

    public async Task<Result<bool>> EnsureRangeIsOpenAsync(DateOnly startDate, DateOnly endDate, CancellationToken ct = default)
    {
        if (startDate > endDate) return Result<bool>.Invalid("dateRange", "The date range is invalid.");
        if (tenant.TenantId is not Guid tenantId || tenantId == Guid.Empty) return Result<bool>.Unauthorized("No authenticated tenant.");
        var closed = await db.AttendancePeriods.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Status == AttendancePeriodStatus.Closed && x.StartDate <= endDate && x.EndDate >= startDate, ct);
        return closed ? Result<bool>.Conflict("The Attendance period is closed and must be reopened before this change.") : Result<bool>.Success(true);
    }
}
