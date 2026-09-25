using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class OvertimeService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock, IAttendanceAuthorizationService? authorization = null) : IOvertimeService
{
    public async Task<Result<OvertimePolicyDto>> CreatePolicyAsync(OvertimePolicyRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<OvertimePolicyDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Code) || request.EffectiveTo < request.EffectiveFrom) return Result<OvertimePolicyDto>.Invalid("Policy code and valid effective dates are required.");
        if (request.MinimumExtraMinutes < 0 || request.RoundingMinutes < 0 || request.MaximumMinutesPerDay < 0 || request.MaximumMinutesPerMonth < 0) return Result<OvertimePolicyDto>.Invalid("OT thresholds and caps cannot be negative.");
        if (await db.OvertimePolicies.AnyAsync(x => x.TenantId == tid && x.Code == request.Code.Trim(), ct)) return Result<OvertimePolicyDto>.Conflict("An OT policy with this code already exists.");
        var entity = new OvertimePolicy { Id = Guid.NewGuid(), TenantId = tid, Code = request.Code.Trim(), Name = request.Name.Trim(), EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, MinimumExtraMinutes = request.MinimumExtraMinutes, RoundingMinutes = request.RoundingMinutes, RoundingMode = request.RoundingMode, MaximumMinutesPerDay = request.MaximumMinutesPerDay, MaximumMinutesPerMonth = request.MaximumMinutesPerMonth, RequirePreApproval = request.RequirePreApproval, RequirePostApproval = request.RequirePostApproval, AllowNormalWorkingDay = request.AllowNormalWorkingDay, AllowWeekOff = request.AllowWeekOff, AllowHoliday = request.AllowHoliday, NormalDayMultiplier = request.NormalDayMultiplier, WeekOffMultiplier = request.WeekOffMultiplier, HolidayMultiplier = request.HolidayMultiplier, EligibilityMode = request.EligibilityMode.Trim(), HourlyRateComponentId = request.HourlyRateComponentId, MonthlyWorkMinutes = request.MonthlyWorkMinutes };
        db.OvertimePolicies.Add(entity); await db.SaveChangesAsync(ct); return Result<OvertimePolicyDto>.Success(ToDto(entity));
    }

    public async Task<Result<OvertimeRequestDto>> CreateRequestAsync(OvertimeRequestRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<OvertimeRequestDto>.Unauthorized("No authenticated tenant.");
        if (tenant.UserId is Guid userId)
        {
            var linkedEmployeeId = await db.AccountEmployeeCurrentLinks.AsNoTracking().Where(x => x.TenantId == tid && x.UserId == userId).Select(x => (Guid?)x.EmployeeId).SingleOrDefaultAsync(ct);
            if (linkedEmployeeId is Guid ownEmployeeId && ownEmployeeId != request.EmployeeId) return Result<OvertimeRequestDto>.Forbidden("Employees may create OT only for themselves.");
        }
        var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == request.EmployeeId, ct); if (employee is null) return Result<OvertimeRequestDto>.NotFound("Employee not found.");
        if (authorization is not null)
        {
            var access = await authorization.CanAccessEmployeeAsync(request.EmployeeId, HRMS.Domain.Authorization.Permissions.Attendance.OvertimeRequest, includeSelf: true, includeManager: true, includeRoleScope: true, request.WorkDate, ct);
            if (!access.Succeeded || access.Value != true) return Result<OvertimeRequestDto>.Failure(access.Status, access.Message, access.Errors);
        }
        var policy = await ResolvePolicyAsync(request.WorkDate, tid, ct); if (policy is null) return Result<OvertimeRequestDto>.Conflict("OvertimePolicyNotFound: no active policy is effective for the work date.");
        if (policy.EligibilityMode.Equals("None", StringComparison.OrdinalIgnoreCase)) return Result<OvertimeRequestDto>.Forbidden("OvertimeNotEligible: employee is not eligible for overtime.");
        if (request.RequestedMinutes <= 0) return Result<OvertimeRequestDto>.Invalid("requestedMinutes", "Requested minutes must be positive.");
        if (request.Category == OvertimeCategory.NormalDay && !policy.AllowNormalWorkingDay || request.Category == OvertimeCategory.WeekOff && !policy.AllowWeekOff || request.Category == OvertimeCategory.Holiday && !policy.AllowHoliday) return Result<OvertimeRequestDto>.Conflict("The selected OT category is not allowed by the effective policy.");
        var day = await db.EmployeeAttendanceDays.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.EmployeeId == request.EmployeeId && x.BusinessDate == request.WorkDate, ct); if (day is null) return Result<OvertimeRequestDto>.Conflict("AttendanceNotResolved: authoritative Attendance is required before requesting OT.");
        var actual = ActualMinutes(day, request.Category); if (actual < policy.MinimumExtraMinutes) actual = 0; actual = ApplyRounding(actual, policy); if (policy.MaximumMinutesPerDay is int cap) actual = Math.Min(actual, cap);
        if (await db.OvertimeRequests.AnyAsync(x => x.TenantId == tid && x.EmployeeId == request.EmployeeId && x.WorkDate == request.WorkDate && (x.Status == OvertimeRequestStatus.Submitted || x.Status == OvertimeRequestStatus.Approved), ct)) return Result<OvertimeRequestDto>.Conflict("An active OT request already exists for this employee and work date.");
        var entity = new OvertimeRequest { Id = Guid.NewGuid(), TenantId = tid, EmployeeId = request.EmployeeId, WorkDate = request.WorkDate, RequestedMinutes = request.RequestedMinutes, ActualEligibleMinutes = Math.Min(request.RequestedMinutes, actual), Category = request.Category, Reason = request.Reason?.Trim(), PolicyId = policy.Id };
        db.OvertimeRequests.Add(entity); await db.SaveChangesAsync(ct); return Result<OvertimeRequestDto>.Success(ToDto(entity));
    }

    public Task<Result<OvertimeRequestDto>> SubmitAsync(Guid id, CancellationToken ct = default) => TransitionAsync(id, OvertimeRequestStatus.Submitted, null, ct);

    public async Task<Result<OvertimeRequestDto>> ApproveAsync(Guid id, OvertimeDecisionRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<OvertimeRequestDto>.Unauthorized("No authenticated tenant.");
        var item = await db.OvertimeRequests.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct); if (item is null) return Result<OvertimeRequestDto>.NotFound("Overtime request not found.");
        if (tenant.UserId is Guid userId && await db.AccountEmployeeCurrentLinks.AnyAsync(x => x.TenantId == tid && x.UserId == userId && x.EmployeeId == item.EmployeeId, ct)) return Result<OvertimeRequestDto>.Forbidden("The requester cannot approve their own OT request.");
        if (authorization is not null)
        {
            var access = await authorization.CanAccessEmployeeAsync(item.EmployeeId, HRMS.Domain.Authorization.Permissions.Attendance.OvertimeApprove, includeSelf: false, includeManager: true, includeRoleScope: true, item.WorkDate, ct);
            if (!access.Succeeded || access.Value != true) return Result<OvertimeRequestDto>.Failure(access.Status, access.Message, access.Errors);
        }
        if (item.Status != OvertimeRequestStatus.Submitted) return Result<OvertimeRequestDto>.Conflict("Only submitted OT requests can be approved.");
        if (request.ExpectedConcurrencyVersion is int v && v != item.ConcurrencyVersion) return Result<OvertimeRequestDto>.Conflict("The OT request changed; reload and retry.");
        item.Status = OvertimeRequestStatus.Approved; item.ApprovedMinutes = Math.Min(item.RequestedMinutes, item.ActualEligibleMinutes); item.ApprovedByUserId = tenant.UserId; item.ApprovedAtUtc = clock.GetUtcNow().UtcDateTime; item.ConcurrencyVersion++;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<OvertimeRequestDto>.Conflict("The OT request changed while approval was in progress."); } return Result<OvertimeRequestDto>.Success(ToDto(item));
    }

    public Task<Result<OvertimeRequestDto>> RejectAsync(Guid id, OvertimeDecisionRequest request, CancellationToken ct = default) => TransitionAsync(id, OvertimeRequestStatus.Rejected, request.Reason, ct);

    public async Task<Result<OvertimeRequestDto>> CorrectAsync(Guid id, OvertimeCorrectionRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<OvertimeRequestDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.ApprovedMinutes < 0) return Result<OvertimeRequestDto>.Invalid("correction", "A non-negative approved minute value and reason are required.");
        var item = await db.OvertimeRequests.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        if (item is null) return Result<OvertimeRequestDto>.NotFound("Overtime request not found.");
        if (authorization is not null)
        {
            var access = await authorization.CanAccessEmployeeAsync(item.EmployeeId, HRMS.Domain.Authorization.Permissions.Attendance.OvertimeApprove, false, true, true, item.WorkDate, ct);
            if (!access.Succeeded || access.Value != true) return Result<OvertimeRequestDto>.Failure(access.Status, access.Message, access.Errors);
        }
        var period = await db.AttendancePeriods.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.StartDate <= item.WorkDate && x.EndDate >= item.WorkDate, ct);
        if (period is null || await db.PayrollOvertimeSnapshots.AnyAsync(x => x.TenantId == tid && x.AttendancePeriodId == period.Id && x.EmployeeId == item.EmployeeId && x.IsCurrent)) return Result<OvertimeRequestDto>.Conflict("Overtime must be reopened before correction.");
        if (request.ExpectedConcurrencyVersion is int v && v != item.ConcurrencyVersion) return Result<OvertimeRequestDto>.Conflict("The OT request changed; reload and retry.");
        item.ApprovedMinutes = Math.Min(request.ApprovedMinutes, item.ActualEligibleMinutes); item.CorrectionReason = request.Reason.Trim(); item.CorrectedByUserId = tenant.UserId; item.CorrectedAtUtc = clock.GetUtcNow().UtcDateTime; item.ConcurrencyVersion++;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<OvertimeRequestDto>.Conflict("The OT request changed while correction was in progress."); }
        return Result<OvertimeRequestDto>.Success(ToDto(item));
    }

    public async Task<Result<IReadOnlyList<PayrollOvertimeSnapshotContract>>> FinalizeAsync(Guid attendancePeriodId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<IReadOnlyList<PayrollOvertimeSnapshotContract>>.Unauthorized("No authenticated tenant.");
        var period = await db.AttendancePeriods.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == attendancePeriodId, ct); if (period is null) return Result<IReadOnlyList<PayrollOvertimeSnapshotContract>>.NotFound("Attendance period not found.");
        if (period.Status != AttendancePeriodStatus.Closed) return Result<IReadOnlyList<PayrollOvertimeSnapshotContract>>.Conflict("OvertimeNotFinalized: Attendance must be finalized before OT finalization.");
        var attendance = await db.PayrollAttendanceSnapshots.AsNoTracking().Where(x => x.TenantId == tid && x.AttendancePeriodId == period.Id && x.IsCurrent).ToListAsync(ct); var requests = await db.OvertimeRequests.Where(x => x.TenantId == tid && x.WorkDate >= period.StartDate && x.WorkDate <= period.EndDate && x.Status == OvertimeRequestStatus.Approved).ToListAsync(ct); var policy = await ResolvePolicyAsync(period.StartDate, tid, ct); var result = new List<PayrollOvertimeSnapshotContract>();
        foreach (var a in attendance)
        {
            var rows = requests.Where(x => x.EmployeeId == a.EmployeeId).ToList(); var normal = rows.Where(x => x.Category == OvertimeCategory.NormalDay).Sum(x => x.ApprovedMinutes); var week = rows.Where(x => x.Category == OvertimeCategory.WeekOff).Sum(x => x.ApprovedMinutes); var holiday = rows.Where(x => x.Category == OvertimeCategory.Holiday).Sum(x => x.ApprovedMinutes); if (policy?.MaximumMinutesPerMonth is int cap) (normal, week, holiday) = ApplyMonthlyCap(normal, week, holiday, cap);
            var prior = await db.PayrollOvertimeSnapshots.Where(x => x.TenantId == tid && x.AttendancePeriodId == period.Id && x.EmployeeId == a.EmployeeId).ToListAsync(ct); foreach (var old in prior) old.IsCurrent = false; var priorSummaries = await db.EmployeeMonthlyOvertimes.Where(x => x.TenantId == tid && x.AttendancePeriodId == period.Id && x.EmployeeId == a.EmployeeId).ToListAsync(ct); foreach (var old in priorSummaries) old.IsCurrent = false; var version = prior.Select(x => x.Version).DefaultIfEmpty(0).Max() + 1;
            var snap = new PayrollOvertimeSnapshot { Id = Guid.NewGuid(), TenantId = tid, AttendancePeriodId = period.Id, EmployeeId = a.EmployeeId, Version = version, IsCurrent = true, AttendanceSnapshotId = a.Id, AttendanceVersion = a.Version, NormalDayMinutes = normal, WeekOffMinutes = week, HolidayMinutes = holiday, TotalApprovedMinutes = normal + week + holiday, NormalDayMultiplier = policy?.NormalDayMultiplier ?? 1m, WeekOffMultiplier = policy?.WeekOffMultiplier ?? 1m, HolidayMultiplier = policy?.HolidayMultiplier ?? 1m, PolicyId = policy?.Id, HourlyRateComponentId = policy?.HourlyRateComponentId, MonthlyWorkMinutes = policy?.MonthlyWorkMinutes, FinalizedByUserId = tenant.UserId, FinalizedAtUtc = clock.GetUtcNow().UtcDateTime }; db.PayrollOvertimeSnapshots.Add(snap); result.Add(ToContract(snap));
            db.EmployeeMonthlyOvertimes.Add(new EmployeeMonthlyOvertime { Id = Guid.NewGuid(), TenantId = tid, AttendancePeriodId = period.Id, EmployeeId = a.EmployeeId, Version = version, IsCurrent = true, IsFinalized = true, AttendanceSnapshotId = a.Id, AttendanceVersion = a.Version, NormalDayMinutes = normal, WeekOffMinutes = week, HolidayMinutes = holiday, TotalApprovedMinutes = normal + week + holiday, PolicyId = policy?.Id, FinalizedByUserId = tenant.UserId, FinalizedAtUtc = snap.FinalizedAtUtc });
        }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<IReadOnlyList<PayrollOvertimeSnapshotContract>>.Conflict("Overtime finalization changed concurrently; retry the operation."); } return Result<IReadOnlyList<PayrollOvertimeSnapshotContract>>.Success(result);
    }

    public async Task<Result<bool>> ReopenAsync(Guid attendancePeriodId, string reason, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<bool>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(reason)) return Result<bool>.Invalid("reason", "A reopen reason is required.");
        var current = await db.PayrollOvertimeSnapshots.Where(x => x.TenantId == tid && x.AttendancePeriodId == attendancePeriodId && x.IsCurrent).ToListAsync(ct);
        if (current.Count == 0) return Result<bool>.NotFound("No current overtime snapshot exists for the Attendance period.");
        foreach (var snapshot in current) { snapshot.IsCurrent = false; snapshot.ReopenReason = reason.Trim(); snapshot.ReopenedByUserId = tenant.UserId; snapshot.ReopenedAtUtc = clock.GetUtcNow().UtcDateTime; }
        var summaries = await db.EmployeeMonthlyOvertimes.Where(x => x.TenantId == tid && x.AttendancePeriodId == attendancePeriodId && x.IsCurrent).ToListAsync(ct);
        foreach (var summary in summaries) summary.IsCurrent = false;
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<bool>.Conflict("Overtime reopen changed concurrently; retry the operation."); }
        return Result<bool>.Success(true);
    }

    public async Task<Result<PayrollOvertimeSnapshotContract?>> ResolveAsync(Guid employeeId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PayrollOvertimeSnapshotContract?>.Unauthorized("No authenticated tenant.");
        var period = await db.AttendancePeriods.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.StartDate == periodStart && x.EndDate == periodEnd, ct); if (period is null || period.Status != AttendancePeriodStatus.Closed) return Result<PayrollOvertimeSnapshotContract?>.Conflict("OvertimeNotFinalized: the Attendance period is not finalized.");
        var snapshot = await db.PayrollOvertimeSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.AttendancePeriodId == period.Id && x.EmployeeId == employeeId && x.IsCurrent, ct);
        if (snapshot is null) return Result<PayrollOvertimeSnapshotContract?>.Success(null);
        var attendance = await db.PayrollAttendanceSnapshots.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.AttendancePeriodId == period.Id && x.EmployeeId == employeeId && x.IsCurrent, ct);
        if (attendance is null || attendance.Id != snapshot.AttendanceSnapshotId || attendance.Version != snapshot.AttendanceVersion) return Result<PayrollOvertimeSnapshotContract?>.Conflict("OvertimeVersionConflict: the OT snapshot was derived from a stale Attendance version.");
        return Result<PayrollOvertimeSnapshotContract?>.Success(ToContract(snapshot));
    }

    private async Task<Result<OvertimeRequestDto>> TransitionAsync(Guid id, OvertimeRequestStatus status, string? reason, CancellationToken ct)
    { if (tenant.TenantId is not Guid tid) return Result<OvertimeRequestDto>.Unauthorized("No authenticated tenant."); var item = await db.OvertimeRequests.FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct); if (item is null) return Result<OvertimeRequestDto>.NotFound("Overtime request not found."); if (status == OvertimeRequestStatus.Submitted && item.Status != OvertimeRequestStatus.Draft) return Result<OvertimeRequestDto>.Conflict("Only draft OT requests can be submitted."); if (status == OvertimeRequestStatus.Rejected && item.Status != OvertimeRequestStatus.Submitted) return Result<OvertimeRequestDto>.Conflict("Only submitted OT requests can be rejected."); item.Status = status; item.RejectionReason = reason; if (status == OvertimeRequestStatus.Submitted) { item.SubmittedByUserId = tenant.UserId; item.SubmittedAtUtc = clock.GetUtcNow().UtcDateTime; } else { item.RejectedByUserId = tenant.UserId; item.RejectedAtUtc = clock.GetUtcNow().UtcDateTime; } item.ConcurrencyVersion++; await db.SaveChangesAsync(ct); return Result<OvertimeRequestDto>.Success(ToDto(item)); }
    private async Task<OvertimePolicy?> ResolvePolicyAsync(DateOnly date, Guid tid, CancellationToken ct) => await db.OvertimePolicies.AsNoTracking().Where(x => x.TenantId == tid && x.IsActive && x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date)).OrderByDescending(x => x.EffectiveFrom).FirstOrDefaultAsync(ct);
    private static int ActualMinutes(EmployeeAttendanceDay day, OvertimeCategory category) => category == OvertimeCategory.NormalDay ? Math.Max(0, (day.WorkedMinutes ?? 0) - (day.ExpectedWorkMinutes ?? 0)) : Math.Max(0, day.WorkedMinutes ?? 0);
    private static (int Normal, int WeekOff, int Holiday) ApplyMonthlyCap(int normal, int weekOff, int holiday, int cap)
    {
        var remaining = Math.Max(0, cap);
        normal = Math.Min(normal, remaining); remaining -= normal;
        weekOff = Math.Min(weekOff, remaining); remaining -= weekOff;
        holiday = Math.Min(holiday, remaining);
        return (normal, weekOff, holiday);
    }
    private static int ApplyRounding(int minutes, OvertimePolicy p) { if (minutes <= 0 || p.RoundingMode == OvertimeRoundingMode.None || p.RoundingMinutes <= 0) return minutes; var q = (decimal)minutes / p.RoundingMinutes; return p.RoundingMode switch { OvertimeRoundingMode.Floor => (int)Math.Floor(q) * p.RoundingMinutes, OvertimeRoundingMode.Ceiling => (int)Math.Ceiling(q) * p.RoundingMinutes, OvertimeRoundingMode.Nearest => (int)Math.Round(q, MidpointRounding.AwayFromZero) * p.RoundingMinutes, _ => minutes }; }
    private static OvertimePolicyDto ToDto(OvertimePolicy x) => new(x.Id, x.Code, x.Name, x.EffectiveFrom, x.EffectiveTo, x.MinimumExtraMinutes, x.RoundingMinutes, x.RoundingMode, x.MaximumMinutesPerDay, x.MaximumMinutesPerMonth, x.AllowNormalWorkingDay, x.AllowWeekOff, x.AllowHoliday, x.NormalDayMultiplier, x.WeekOffMultiplier, x.HolidayMultiplier, x.EligibilityMode, x.HourlyRateComponentId, x.MonthlyWorkMinutes);
    private static OvertimeRequestDto ToDto(OvertimeRequest x) => new(x.Id, x.EmployeeId, x.WorkDate, x.RequestedMinutes, x.ActualEligibleMinutes, x.ApprovedMinutes, x.Category, x.Status, x.PolicyId, x.Reason, x.ConcurrencyVersion);
    private static PayrollOvertimeSnapshotContract ToContract(PayrollOvertimeSnapshot x) => new(x.Id, x.Version, x.AttendanceSnapshotId, x.AttendanceVersion, x.NormalDayMinutes, x.WeekOffMinutes, x.HolidayMinutes, x.TotalApprovedMinutes, x.NormalDayMultiplier, x.WeekOffMultiplier, x.HolidayMultiplier, x.HourlyRateComponentId, x.MonthlyWorkMinutes);
}
