using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace HRMS.Application.Services;

/// <summary>
/// Comp-Off entitlement orchestration. Comp-Off is minute based and append-only; Leave remains the
/// workflow owner while this service owns source credits, allocations, and ledger invariants.
/// </summary>
public sealed class CompOffService(IHrmsDbContext db, ITenantContext tenant, TimeProvider clock, IAttendanceAuthorizationService? authorization = null) : ICompOffService
{
    public async Task<Result<CompOffPolicyDto>> CreatePolicyAsync(CompOffPolicyRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<CompOffPolicyDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Code) || request.EffectiveTo < request.EffectiveFrom)
            return Result<CompOffPolicyDto>.Invalid("A policy code and valid effective dates are required.");
        if (request.MinimumWorkedMinutes < 0 || request.CreditRatio <= 0 || request.RoundingMinutes < 0 || request.ConsumptionIncrementMinutes <= 0)
            return Result<CompOffPolicyDto>.Invalid("Threshold, ratio, rounding, and consumption values are invalid.");
        if (await db.CompOffPolicies.AnyAsync(x => x.TenantId == tid && x.Code == request.Code.Trim(), ct))
            return Result<CompOffPolicyDto>.Conflict("A Comp-Off policy with this code already exists.");
        var entity = new CompOffPolicy
        {
            Id = Guid.NewGuid(), TenantId = tid, Code = request.Code.Trim(), Name = request.Name.Trim(),
            EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
            AllowWeekOff = request.AllowWeekOff, AllowHoliday = request.AllowHoliday,
            AllowOvertimeSource = request.AllowOvertimeSource, MinimumWorkedMinutes = request.MinimumWorkedMinutes,
            CreditRatio = request.CreditRatio, RoundingMode = request.RoundingMode, RoundingMinutes = request.RoundingMinutes,
            MaximumCreditMinutesPerDay = request.MaximumCreditMinutesPerDay, MaximumCreditMinutesPerMonth = request.MaximumCreditMinutesPerMonth,
            ExpiryDays = request.ExpiryDays, ExpiryMonths = request.ExpiryMonths, RequireCreditApproval = request.RequireCreditApproval,
            AllowPartialDayConsumption = request.AllowPartialDayConsumption, ConsumptionIncrementMinutes = request.ConsumptionIncrementMinutes,
            BenefitMode = request.BenefitMode, EligibilityMode = request.EligibilityMode.Trim()
        };
        db.CompOffPolicies.Add(entity); await db.SaveChangesAsync(ct); return Result<CompOffPolicyDto>.Success(ToDto(entity));
    }

    public async Task<Result<CompOffEarningDto>> EarnAsync(CompOffEarnRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<CompOffEarningDto>.Unauthorized("No authenticated tenant.");
        var day = await db.EmployeeAttendanceDays.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tid && x.Id == request.AttendanceDayId && x.EmployeeId == request.EmployeeId && x.BusinessDate == request.WorkDate, ct);
        if (day is null) return Result<CompOffEarningDto>.NotFound("The authoritative Attendance day was not found.");
        if (day.Status == EmployeeAttendanceDayStatus.Incomplete || day.Status == EmployeeAttendanceDayStatus.NotProcessed || day.WorkedMinutes is null)
            return Result<CompOffEarningDto>.Conflict("Comp-Off requires a resolved authoritative Attendance day.");
        var policy = await db.CompOffPolicies.Where(x => x.TenantId == tid && x.IsActive && x.EffectiveFrom <= request.WorkDate && (x.EffectiveTo == null || x.EffectiveTo >= request.WorkDate)).OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (policy is null) return Result<CompOffEarningDto>.Conflict("CompOffPolicyNotFound: no policy is effective for the source work date.");
        if (policy.EligibilityMode.Equals("None", StringComparison.OrdinalIgnoreCase)) return Result<CompOffEarningDto>.Forbidden("CompOffNotEligible: the employee is not eligible.");
        if (request.SourceType == CompOffSourceType.WeekOff && (!policy.AllowWeekOff || day.RosterDayType != RosterDayType.WeeklyOff)) return Result<CompOffEarningDto>.Conflict("The source is not an eligible Week-Off event.");
        if (request.SourceType == CompOffSourceType.Holiday && (!policy.AllowHoliday || day.RosterDayType != RosterDayType.Holiday)) return Result<CompOffEarningDto>.Conflict("The source is not an eligible Holiday event.");
        if (request.SourceType == CompOffSourceType.Overtime && !policy.AllowOvertimeSource) return Result<CompOffEarningDto>.Conflict("The policy does not allow an Overtime source.");
        if (policy.BenefitMode == CompOffBenefitMode.OvertimeOnly || (request.SourceOvertimeSnapshotId is not null && policy.BenefitMode != CompOffBenefitMode.DualBenefit)) return Result<CompOffEarningDto>.Conflict("DoubleBenefitDenied: the source is already committed to monetary Overtime.");
        var sourceKey = $"Attendance:{request.AttendanceDayId:D}:v{request.SourceAttendanceVersion}";
        var existing = await db.CompOffEarnings.SingleOrDefaultAsync(x => x.TenantId == tid && x.SourceKey == sourceKey, ct);
        if (existing is not null) return Result<CompOffEarningDto>.Success(ToDto(existing), "The source credit already exists.");
        var sourceMinutes = Math.Max(0, day.WorkedMinutes.Value);
        var eligible = sourceMinutes < policy.MinimumWorkedMinutes ? 0 : sourceMinutes;
        var credited = Round((int)Math.Floor(eligible * policy.CreditRatio), policy);
        if (policy.MaximumCreditMinutesPerDay is int dailyCap) credited = Math.Min(credited, dailyCap);
        if (credited <= 0) return Result<CompOffEarningDto>.Conflict("CompOffNotEligible: the source is below the configured threshold.");
        var monthStart = new DateOnly(request.WorkDate.Year, request.WorkDate.Month, 1); var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        if (policy.MaximumCreditMinutesPerMonth is int monthlyCap)
        {
            var prior = await db.CompOffEarnings.Where(x => x.TenantId == tid && x.EmployeeId == request.EmployeeId && x.SourceWorkDate >= monthStart && x.SourceWorkDate <= monthEnd && x.Status != CompOffEarningStatus.Rejected).SumAsync(x => (int?)x.CreditedMinutes, ct) ?? 0;
            credited = Math.Min(credited, Math.Max(0, monthlyCap - prior));
        }
        if (credited <= 0) return Result<CompOffEarningDto>.Conflict("The monthly Comp-Off credit cap has been reached.");
        DateOnly? expiry = policy.ExpiryDays is int days ? request.WorkDate.AddDays(days) : policy.ExpiryMonths is int months ? request.WorkDate.AddMonths(months) : null;
        var earning = new CompOffEarning { Id = Guid.NewGuid(), TenantId = tid, EmployeeId = request.EmployeeId, SourceWorkDate = request.WorkDate, SourceType = request.SourceType, SourceAttendanceDayId = request.AttendanceDayId, SourceAttendanceVersion = request.SourceAttendanceVersion, SourceOvertimeRequestId = request.SourceOvertimeRequestId, SourceOvertimeSnapshotId = request.SourceOvertimeSnapshotId, PolicyId = policy.Id, PolicyVersion = policy.ConcurrencyVersion, SourceWorkedMinutes = sourceMinutes, EligibleMinutes = eligible, CreditedMinutes = credited, Status = policy.RequireCreditApproval ? CompOffEarningStatus.PendingApproval : CompOffEarningStatus.Approved, ExpiresOn = expiry, SourceKey = sourceKey };
        db.CompOffEarnings.Add(earning);
        if (earning.Status == CompOffEarningStatus.Approved) AddLedger(earning, null, CompOffLedgerEntryType.Credit, credited, request.WorkDate, expiry, $"CompOffEarning:{earning.Id:D}", $"credit:{sourceKey}");
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<CompOffEarningDto>.Conflict("Comp-Off source changed concurrently; retry the operation."); } catch (DbUpdateException) { return Result<CompOffEarningDto>.Conflict("The Comp-Off source credit already exists or changed concurrently."); } return Result<CompOffEarningDto>.Success(ToDto(earning));
    }

    public Task<Result<CompOffEarningDto>> ApproveAsync(Guid earningId, CancellationToken ct = default) => DecideAsync(earningId, true, ct);
    public Task<Result<CompOffEarningDto>> RejectAsync(Guid earningId, CancellationToken ct = default) => DecideAsync(earningId, false, ct);

    private async Task<Result<CompOffEarningDto>> DecideAsync(Guid id, bool approve, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tid) return Result<CompOffEarningDto>.Unauthorized("No authenticated tenant.");
        var earning = await db.CompOffEarnings.SingleOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct); if (earning is null) return Result<CompOffEarningDto>.NotFound("Comp-Off earning not found.");
        if (earning.Status != CompOffEarningStatus.PendingApproval) return Result<CompOffEarningDto>.Conflict("Only pending Comp-Off credits can be decided.");
        earning.Status = approve ? CompOffEarningStatus.Approved : CompOffEarningStatus.Rejected; earning.ApprovedAtUtc = approve ? clock.GetUtcNow().UtcDateTime : null; earning.ApprovedByUserId = approve ? tenant.UserId : null; earning.ConcurrencyVersion++;
        if (approve) AddLedger(earning, null, CompOffLedgerEntryType.Credit, earning.CreditedMinutes, earning.SourceWorkDate, earning.ExpiresOn, $"CompOffEarning:{earning.Id:D}", $"credit:{earning.SourceKey}");
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<CompOffEarningDto>.Conflict("The Comp-Off credit changed while approval was in progress."); } catch (DbUpdateException) { return Result<CompOffEarningDto>.Conflict("The Comp-Off credit activation already exists or changed concurrently."); } return Result<CompOffEarningDto>.Success(ToDto(earning));
    }

    public async Task<Result<CompOffBalanceDto>> GetBalanceAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<CompOffBalanceDto>.Unauthorized("No authenticated tenant.");
        var entries = await db.CompOffLedgerEntries.AsNoTracking().Where(x => x.TenantId == tid && x.EmployeeId == employeeId).ToListAsync(ct); return Result<CompOffBalanceDto>.Success(Balance(employeeId, entries, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)));
    }

    public async Task<Result<IReadOnlyList<CompOffEarningDto>>> GetEarningsAsync(Guid employeeId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<IReadOnlyList<CompOffEarningDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.CompOffEarnings.AsNoTracking().Where(x => x.TenantId == tid && x.EmployeeId == employeeId).OrderByDescending(x => x.SourceWorkDate).ToListAsync(ct);
        return Result<IReadOnlyList<CompOffEarningDto>>.Success(rows.Select(ToDto).ToList());
    }

    public async Task<Result<IReadOnlyList<CompOffLedgerDto>>> GetLedgerAsync(Guid employeeId, CancellationToken ct = default) =>
        tenant.TenantId is not Guid tid ? Result<IReadOnlyList<CompOffLedgerDto>>.Unauthorized("No authenticated tenant.") : Result<IReadOnlyList<CompOffLedgerDto>>.Success(await db.CompOffLedgerEntries.AsNoTracking().Where(x => x.TenantId == tid && x.EmployeeId == employeeId).OrderBy(x => x.EffectiveDate).ThenBy(x => x.Id).Select(x => new CompOffLedgerDto(x.Id, x.EarningId, x.LeaveRequestId, x.EntryType, x.Minutes, x.EffectiveDate, x.ExpiresOn, x.SourceReference)).ToListAsync(ct));

    public async Task<Result<PagedResult<CompOffOperationalEarningDto>>> GetOperationalAsync(CompOffOperationalQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<PagedResult<CompOffOperationalEarningDto>>.Unauthorized("No authenticated tenant.");
        if (query.Page < 1 || query.PageSize is < 1 or > PagedQuery.MaxPageSize) return Result<PagedResult<CompOffOperationalEarningDto>>.Invalid("page", "Page values are out of range.");
        if (query.FromDate is DateOnly from && query.ToDate is DateOnly to && from > to) return Result<PagedResult<CompOffOperationalEarningDto>>.Invalid("dateRange", "FromDate cannot be after ToDate.");

        var effectiveDate = query.ToDate ?? query.FromDate ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var scope = await BuildOperationalScopeAsync(effectiveDate, ct);
        if (!scope.Succeeded || scope.Value is null) return Result<PagedResult<CompOffOperationalEarningDto>>.Failure(scope.Status, scope.Message, scope.Errors);

        var employees = db.Employees.AsNoTracking().Where(x => x.TenantId == tid).Where(scope.Value);
        if (query.EmployeeId is Guid employeeId) employees = employees.Where(x => x.Id == employeeId);
        if (!string.IsNullOrWhiteSpace(query.EmployeeCode)) employees = employees.Where(x => x.EmployeeCode != null && x.EmployeeCode.Contains(query.EmployeeCode.Trim()));
        if (query.DepartmentId is Guid departmentId) employees = employees.Where(x => x.DepartmentId == departmentId || x.EmploymentHistory.Any(h => h.DepartmentId == departmentId && !h.IsSuperseded && h.EffectiveFrom <= effectiveDate && (h.EffectiveTo == null || h.EffectiveTo >= effectiveDate)));
        if (query.WorkLocationId is Guid workLocationId) employees = employees.Where(x => x.EmploymentHistory.Any(h => h.WorkLocationId == workLocationId && !h.IsSuperseded && h.EffectiveFrom <= effectiveDate && (h.EffectiveTo == null || h.EffectiveTo >= effectiveDate)));
        if (query.ManagerId is Guid managerId) employees = employees.Where(x => x.ReportingManagerId == managerId || x.EmploymentHistory.Any(h => h.ManagerId == managerId && !h.IsSuperseded && h.EffectiveFrom <= effectiveDate && (h.EffectiveTo == null || h.EffectiveTo >= effectiveDate)));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            employees = employees.Where(x => (x.EmployeeCode != null && x.EmployeeCode.Contains(search)) || (x.FirstName + " " + x.LastName).Contains(search));
        }

        var rows = from earning in db.CompOffEarnings.AsNoTracking()
                   join employee in employees on new { earning.TenantId, earning.EmployeeId } equals new { employee.TenantId, EmployeeId = employee.Id }
                   where earning.TenantId == tid
                   select new
                   {
                       earning.Id,
                       earning.EmployeeId,
                       EmployeeCode = employee.EmployeeCode ?? string.Empty,
                       EmployeeName = (employee.FirstName + " " + employee.LastName).Trim(),
                       WorkDate = earning.SourceWorkDate,
                       earning.SourceType,
                       EligibleWorkedMinutes = earning.EligibleMinutes,
                       earning.CreditedMinutes,
                       ExpiryDate = earning.ExpiresOn,
                       earning.Status,
                       earning.PolicyId,
                       earning.PolicyVersion,
                       AttendanceDayId = earning.SourceAttendanceDayId,
                       AttendanceVersion = earning.SourceAttendanceVersion,
                       CorrectionStatus = (string?)null,
                       CorrectionDeficitMinutes = (int?)null,
                       ApprovalRequired = earning.Policy != null && earning.Policy.RequireCreditApproval,
                       earning.ApprovedByUserId,
                       earning.ApprovedAtUtc
                   };
        if (query.FromDate is DateOnly fromDate) rows = rows.Where(x => x.WorkDate >= fromDate);
        if (query.ToDate is DateOnly toDate) rows = rows.Where(x => x.WorkDate <= toDate);
        if (query.SourceType is CompOffSourceType sourceType) rows = rows.Where(x => x.SourceType == sourceType);
        if (query.Status is CompOffEarningStatus status) rows = rows.Where(x => x.Status == status);
        if (query.ExpiryFrom is DateOnly expiryFrom) rows = rows.Where(x => x.ExpiryDate >= expiryFrom);
        if (query.ExpiryTo is DateOnly expiryTo) rows = rows.Where(x => x.ExpiryDate <= expiryTo);

        var total = await rows.CountAsync(ct);
        var pageRows = await rows.OrderBy(x => x.WorkDate).ThenBy(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var earningIds = pageRows.Select(x => x.Id).ToArray();
        var ledger = await db.CompOffLedgerEntries.AsNoTracking().Where(x => x.TenantId == tid && earningIds.Contains(x.EarningId)).ToListAsync(ct);
        var asOfDate = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var balances = ledger.GroupBy(x => x.EarningId).ToDictionary(x => x.Key, x => LedgerState(x, asOfDate));
        var items = pageRows.Select(x =>
        {
            var state = balances.GetValueOrDefault(x.Id);
            return new CompOffOperationalEarningDto(x.Id, x.EmployeeId, x.EmployeeCode, x.EmployeeName, x.WorkDate, x.SourceType, x.EligibleWorkedMinutes, x.CreditedMinutes, state.Available, state.Reserved, state.Consumed, state.Expired, x.ExpiryDate, x.Status, x.PolicyId, x.PolicyVersion, x.AttendanceDayId, x.AttendanceVersion, x.CorrectionStatus, x.CorrectionDeficitMinutes, x.ApprovalRequired, x.ApprovedByUserId, x.ApprovedAtUtc);
        }).ToList();
        return Result<PagedResult<CompOffOperationalEarningDto>>.Success(new PagedResult<CompOffOperationalEarningDto>(items, query.Page, query.PageSize, total));
    }

    private async Task<Result<Expression<Func<Employee, bool>>>> BuildOperationalScopeAsync(DateOnly effectiveDate, CancellationToken ct)
    {
        if (authorization is null) return Result<Expression<Func<Employee, bool>>>.Forbidden("Comp-Off operational authorization is not configured.");
        Result<Expression<Func<Employee, bool>>>? last = null;
        foreach (var permission in new[] { HRMS.Domain.Authorization.Permissions.Attendance.CompOffViewAll, HRMS.Domain.Authorization.Permissions.Attendance.CompOffViewTeam, HRMS.Domain.Authorization.Permissions.Attendance.CompOffManage, HRMS.Domain.Authorization.Permissions.Attendance.CompOffApprove, HRMS.Domain.Authorization.Permissions.Attendance.CompOffViewHistory })
        {
            var result = await authorization.BuildEmployeePredicateAsync(permission, includeSelf: false, includeManager: true, includeRoleScope: true, effectiveDate, ct);
            if (result.Succeeded) return result;
            last = result;
        }
        return last ?? Result<Expression<Func<Employee, bool>>>.Forbidden("Comp-Off operational permission is required.");
    }


    public Task<Result<bool>> ReserveAsync(Guid leaveRequestId, Guid employeeId, int minutes, CancellationToken ct = default) => ApplyLeaveAsync(leaveRequestId, employeeId, minutes, CompOffLedgerEntryType.Reserve, ct);
    public Task<Result<bool>> ConsumeAsync(Guid leaveRequestId, CancellationToken ct = default) => TransitionLeaveAsync(leaveRequestId, CompOffLedgerEntryType.Consume, ct);
    public Task<Result<bool>> ReleaseAsync(Guid leaveRequestId, CancellationToken ct = default) => TransitionLeaveAsync(leaveRequestId, CompOffLedgerEntryType.Release, ct);
    public Task<Result<bool>> RestoreAsync(Guid leaveRequestId, CancellationToken ct = default) => TransitionLeaveAsync(leaveRequestId, CompOffLedgerEntryType.Restore, ct);

    private async Task<Result<bool>> ApplyLeaveAsync(Guid leaveRequestId, Guid employeeId, int minutes, CompOffLedgerEntryType type, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tid) return Result<bool>.Unauthorized("No authenticated tenant."); if (minutes <= 0) return Result<bool>.Invalid("minutes", "Minutes must be positive.");
        var current = await db.CompOffLeaveAllocations.AnyAsync(x => x.TenantId == tid && x.LeaveRequestId == leaveRequestId && x.Status == "Reserved", ct); if (current) return Result<bool>.Success(true, "Reservation already exists.");
        var earnings = (await db.CompOffEarnings.Where(x => x.TenantId == tid && x.EmployeeId == employeeId && x.Status == CompOffEarningStatus.Approved).ToListAsync(ct))
            .OrderBy(x => x.ExpiresOn ?? DateOnly.MaxValue).ThenBy(x => x.SourceWorkDate).ThenBy(x => x.Id).ToList();
        var ledger = await db.CompOffLedgerEntries.AsNoTracking().Where(x => x.TenantId == tid && x.EmployeeId == employeeId).ToListAsync(ct);
        var used = ledger.GroupBy(x => x.EarningId).ToDictionary(x => x.Key, x => LedgerState(x, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)).Available);
        if (earnings.Sum(x => Math.Max(0, used.GetValueOrDefault(x.Id))) < minutes) return Result<bool>.Conflict("InsufficientCompOffBalance: available Comp-Off is insufficient.");
        var remaining = minutes; foreach (var earning in earnings) { var amount = Math.Min(remaining, Math.Max(0, used.GetValueOrDefault(earning.Id))); if (amount <= 0) continue; var allocation = new CompOffLeaveAllocation { Id = Guid.NewGuid(), TenantId = tid, LeaveRequestId = leaveRequestId, EarningId = earning.Id, ReservedMinutes = amount }; db.CompOffLeaveAllocations.Add(allocation); earning.ConcurrencyVersion++; AddLedger(earning, leaveRequestId, type, amount, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), earning.ExpiresOn, $"LeaveRequest:{leaveRequestId:D}", $"reserve:{leaveRequestId:D}:{earning.Id:D}"); remaining -= amount; if (remaining == 0) break; }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<bool>.Conflict("Comp-Off balance changed concurrently; retry the reservation."); } catch (DbUpdateException) { return Result<bool>.Conflict("The Comp-Off reservation already exists or changed concurrently."); } return Result<bool>.Success(true);
    }

    private async Task<Result<bool>> TransitionLeaveAsync(Guid leaveRequestId, CompOffLedgerEntryType type, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tid) return Result<bool>.Unauthorized("No authenticated tenant.");
        var allocations = await db.CompOffLeaveAllocations.Where(x => x.TenantId == tid && x.LeaveRequestId == leaveRequestId).Include(x => x.Earning).ToListAsync(ct); if (allocations.Count == 0) return Result<bool>.NotFound("Comp-Off leave reservation was not found.");
        foreach (var allocation in allocations) { var amount = type == CompOffLedgerEntryType.Consume ? allocation.ReservedMinutes : type == CompOffLedgerEntryType.Release ? allocation.ReservedMinutes : allocation.ConsumedMinutes; if (amount <= 0) continue; if (type == CompOffLedgerEntryType.Consume) { allocation.ConsumedMinutes += amount; allocation.ReservedMinutes = 0; allocation.Status = "Consumed"; } else if (type == CompOffLedgerEntryType.Release) { allocation.ReleasedMinutes += amount; allocation.ReservedMinutes = 0; allocation.Status = "Released"; } else { allocation.ConsumedMinutes -= amount; allocation.Status = "Restored"; } allocation.Earning!.ConcurrencyVersion++; AddLedger(allocation.Earning!, leaveRequestId, type, amount, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), allocation.Earning!.ExpiresOn, $"LeaveRequest:{leaveRequestId:D}", $"{type}:{leaveRequestId:D}:{allocation.EarningId:D}"); }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<bool>.Conflict("The Comp-Off leave transition changed concurrently; retry the operation."); } catch (DbUpdateException) { return Result<bool>.Conflict("The Comp-Off leave transition already exists or changed concurrently."); } return Result<bool>.Success(true);
    }

    public async Task<Result<int>> ExpireAsync(DateOnly asOfDate, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<int>.Unauthorized("No authenticated tenant."); var count = 0;
        var earnings = await db.CompOffEarnings.Where(x => x.TenantId == tid && x.Status == CompOffEarningStatus.Approved && x.ExpiresOn != null && x.ExpiresOn <= asOfDate).ToListAsync(ct);
        foreach (var earning in earnings) { var existing = await db.CompOffLedgerEntries.Where(x => x.TenantId == tid && x.EarningId == earning.Id).ToListAsync(ct); var state = LedgerState(existing, asOfDate); var available = state.Available; if (available <= 0) continue; AddLedger(earning, null, CompOffLedgerEntryType.Expire, available, asOfDate, earning.ExpiresOn, $"CompOffEarning:{earning.Id:D}", $"expire:{earning.Id:D}"); if (state.Reserved == 0) earning.Status = CompOffEarningStatus.Expired; earning.ConcurrencyVersion++; count++; }
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<int>.Conflict("Comp-Off expiry changed concurrently; retry the operation."); } catch (DbUpdateException) { return Result<int>.Conflict("The Comp-Off expiry transition already exists or changed concurrently."); } return Result<int>.Success(count);
    }

    public async Task<Result<CompOffEarningDto>> CorrectSourceAsync(Guid earningId, int correctedEligibleMinutes, string reason, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid) return Result<CompOffEarningDto>.Unauthorized("No authenticated tenant."); if (correctedEligibleMinutes < 0 || string.IsNullOrWhiteSpace(reason)) return Result<CompOffEarningDto>.Invalid("correction", "A non-negative eligible value and reason are required.");
        var old = await db.CompOffEarnings.SingleOrDefaultAsync(x => x.TenantId == tid && x.Id == earningId, ct); if (old is null) return Result<CompOffEarningDto>.NotFound("Comp-Off earning not found.");
        var ledger = await db.CompOffLedgerEntries.Where(x => x.TenantId == tid && x.EarningId == old.Id).ToListAsync(ct); var consumed = ledger.Where(x => x.EntryType == CompOffLedgerEntryType.Consume).Sum(x => x.Minutes); var newCredit = Math.Max(0, correctedEligibleMinutes); if (newCredit < consumed) return Result<CompOffEarningDto>.Conflict("CorrectionRequiresAdjustment: consumed Comp-Off history cannot be silently reduced.");
        old.Status = CompOffEarningStatus.Superseded; old.ConcurrencyVersion++; var replacement = new CompOffEarning { Id = Guid.NewGuid(), TenantId = tid, EmployeeId = old.EmployeeId, SourceWorkDate = old.SourceWorkDate, SourceType = old.SourceType, SourceAttendanceDayId = old.SourceAttendanceDayId, SourceAttendanceSnapshotId = old.SourceAttendanceSnapshotId, SourceAttendanceVersion = old.SourceAttendanceVersion + 1, SourceOvertimeRequestId = old.SourceOvertimeRequestId, SourceOvertimeSnapshotId = old.SourceOvertimeSnapshotId, PolicyId = old.PolicyId, PolicyVersion = old.PolicyVersion, SourceWorkedMinutes = old.SourceWorkedMinutes, EligibleMinutes = correctedEligibleMinutes, CreditedMinutes = newCredit, Status = CompOffEarningStatus.Approved, ExpiresOn = old.ExpiresOn, SourceKey = $"{old.SourceKey}:correction:{old.Id:D}" }; db.CompOffEarnings.Add(replacement); if (old.CreditedMinutes > newCredit) AddLedger(old, null, CompOffLedgerEntryType.Reversal, old.CreditedMinutes - newCredit, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), old.ExpiresOn, reason.Trim(), $"correction:{old.Id:D}"); else if (newCredit > old.CreditedMinutes) AddLedger(replacement, null, CompOffLedgerEntryType.Credit, newCredit - old.CreditedMinutes, replacement.SourceWorkDate, replacement.ExpiresOn, reason.Trim(), $"correction-credit:{old.Id:D}"); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<CompOffEarningDto>.Conflict("The Comp-Off source changed concurrently; retry the correction."); } catch (DbUpdateException) { return Result<CompOffEarningDto>.Conflict("The Comp-Off correction already exists or changed concurrently."); } return Result<CompOffEarningDto>.Success(ToDto(replacement));
    }

    private void AddLedger(CompOffEarning earning, Guid? leaveRequestId, CompOffLedgerEntryType type, int minutes, DateOnly effectiveDate, DateOnly? expiry, string reference, string key) => db.CompOffLedgerEntries.Add(new CompOffLedgerEntry { Id = Guid.NewGuid(), TenantId = earning.TenantId, EmployeeId = earning.EmployeeId, EarningId = earning.Id, LeaveRequestId = leaveRequestId, EntryType = type, Minutes = minutes, EffectiveDate = effectiveDate, ExpiresOn = expiry, SourceReference = reference, IdempotencyKey = key });
    private static int Round(int minutes, CompOffPolicy p) => p.RoundingMode switch { CompOffRoundingMode.Floor when p.RoundingMinutes > 0 => minutes / p.RoundingMinutes * p.RoundingMinutes, CompOffRoundingMode.Ceiling when p.RoundingMinutes > 0 => (minutes + p.RoundingMinutes - 1) / p.RoundingMinutes * p.RoundingMinutes, CompOffRoundingMode.Nearest when p.RoundingMinutes > 0 => (int)(Math.Round(minutes / (decimal)p.RoundingMinutes, MidpointRounding.AwayFromZero) * p.RoundingMinutes), _ => minutes };
    private static CompOffBalanceDto Balance(Guid employeeId, IReadOnlyCollection<CompOffLedgerEntry> e, DateOnly asOfDate)
    {
        var states = e.GroupBy(x => x.EarningId).Select(x => LedgerState(x, asOfDate)).ToList();
        return new(employeeId, e.Where(x => x.EntryType == CompOffLedgerEntryType.Credit).Sum(x => x.Minutes), Math.Max(0, states.Sum(x => x.Available)), Math.Max(0, states.Sum(x => x.Reserved)), Math.Max(0, states.Sum(x => x.Consumed)), Math.Max(0, states.Sum(x => x.Expired)));
    }

    private static (int Available, int Reserved, int Consumed, int Expired) LedgerState(IEnumerable<CompOffLedgerEntry> entries, DateOnly asOfDate)
    {
        var e = entries.ToList();
        var credit = e.Where(x => x.EntryType == CompOffLedgerEntryType.Credit).Sum(x => x.Minutes);
        var validRestore = e.Where(x => x.EntryType == CompOffLedgerEntryType.Restore && (x.ExpiresOn is null || x.ExpiresOn > asOfDate)).Sum(x => x.Minutes);
        var expiredRestore = e.Where(x => x.EntryType == CompOffLedgerEntryType.Restore && x.ExpiresOn is not null && x.ExpiresOn <= asOfDate).Sum(x => x.Minutes);
        var reserved = Math.Max(0, e.Where(x => x.EntryType == CompOffLedgerEntryType.Reserve).Sum(x => x.Minutes) - e.Where(x => x.EntryType is CompOffLedgerEntryType.Release or CompOffLedgerEntryType.Consume).Sum(x => x.Minutes));
        var consumedEntries = e.Where(x => x.EntryType == CompOffLedgerEntryType.Consume).Sum(x => x.Minutes);
        var consumed = Math.Max(0, consumedEntries - e.Where(x => x.EntryType == CompOffLedgerEntryType.Restore).Sum(x => x.Minutes));
        var expired = e.Where(x => x.EntryType == CompOffLedgerEntryType.Expire).Sum(x => x.Minutes) + expiredRestore;
        var available = Math.Max(0, credit + validRestore - reserved - consumedEntries - expired - e.Where(x => x.EntryType == CompOffLedgerEntryType.Reversal).Sum(x => x.Minutes));
        return (available, reserved, consumed, expired);
    }
    private static CompOffPolicyDto ToDto(CompOffPolicy x) => new(x.Id, x.Code, x.Name, x.EffectiveFrom, x.EffectiveTo, x.MinimumWorkedMinutes, x.CreditRatio, x.RoundingMode, x.MaximumCreditMinutesPerDay, x.MaximumCreditMinutesPerMonth, x.ExpiryDays, x.ExpiryMonths, x.RequireCreditApproval, x.BenefitMode);
    private static CompOffEarningDto ToDto(CompOffEarning x) => new(x.Id, x.EmployeeId, x.SourceWorkDate, x.SourceType, x.SourceWorkedMinutes, x.EligibleMinutes, x.CreditedMinutes, x.Status, x.ExpiresOn, x.PolicyId, x.PolicyVersion, x.SourceAttendanceDayId, x.SourceAttendanceVersion);
}
