using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Attendance;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendanceFoundationService(IHrmsDbContext db, ITenantContext tenant, IEffectiveEmploymentResolver employmentResolver, IWorkingDayCalendarResolver? calendarResolver = null, TimeProvider? timeProvider = null, IAttendancePeriodLockService? periodLock = null) : IAttendanceFoundationService
{
    private const int MaxUploadRows = 100_000;
    private bool TryTenant(out Guid id) { id = tenant.TenantId ?? Guid.Empty; return id != Guid.Empty; }

    public async Task<Result<IReadOnlyList<ShiftDto>>> GetShiftsAsync(ShiftQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<IReadOnlyList<ShiftDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.Shifts.AsNoTracking().Include(x => x.Breaks).Where(x => x.TenantId == tid && (!query.IsActive.HasValue || x.IsActive == query.IsActive)).OrderBy(x => x.ShiftCode).ToListAsync(ct);
        return Result<IReadOnlyList<ShiftDto>>.Success(rows.Select(ToDto).ToList());
    }

    public async Task<Result<ShiftDto>> CreateShiftAsync(ShiftRequest r, CancellationToken ct = default) => await SaveShiftAsync(null, r, ct);
    public async Task<Result<ShiftDto>> UpdateShiftAsync(Guid id, ShiftRequest r, CancellationToken ct = default) => await SaveShiftAsync(id, r, ct);
    private async Task<Result<ShiftDto>> SaveShiftAsync(Guid? id, ShiftRequest r, CancellationToken ct)
    {
        if (!TryTenant(out var tid)) return Result<ShiftDto>.Unauthorized("No authenticated tenant.");
        var error = ValidateShift(r); if (error is not null) return Result<ShiftDto>.Invalid(error.Value.field, error.Value.message);
        var sourceError = ValidateCaptureSources(r); if (sourceError is not null) return Result<ShiftDto>.Invalid(sourceError.Value.field, sourceError.Value.message);
        if (periodLock is not null && !(await periodLock.EnsureRangeIsOpenAsync(r.EffectiveFrom, r.EffectiveTo ?? DateOnly.MaxValue, ct)).Succeeded) return Result<ShiftDto>.Conflict("The effective Shift interval intersects a closed Attendance period.");
        if (r.Breaks.GroupBy(x => x.Sequence).Any(g => g.Count() > 1) || r.Breaks.Any(x => string.IsNullOrWhiteSpace(x.Name) || x.EndTime <= x.StartTime)) return Result<ShiftDto>.Invalid("breaks", "Breaks require unique sequence values and an end time after the start time.");
        if (await db.Shifts.AnyAsync(x => x.TenantId == tid && x.ShiftCode == r.ShiftCode.Trim() && x.Id != id, ct)) return Result<ShiftDto>.Conflict("Shift code already exists in this tenant.");
        if (r.IsDefault && await db.Shifts.AnyAsync(x => x.TenantId == tid && x.IsDefault && x.Id != id && x.EffectiveFrom <= (r.EffectiveTo ?? DateOnly.MaxValue) && (x.EffectiveTo == null || x.EffectiveTo >= r.EffectiveFrom), ct)) return Result<ShiftDto>.Conflict("Another default Shift overlaps this effective interval.");
        Shift item;
        if (id is Guid existing) { item = await db.Shifts.Include(x => x.Breaks).FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == existing, ct) ?? null!; if (item is null) return Result<ShiftDto>.NotFound("Shift was not found."); if (!TokenMatches(item, r.ConcurrencyToken)) return Result<ShiftDto>.Conflict("Shift changed by another user. Reload before saving."); }
        else { item = new Shift { Id = Guid.NewGuid(), TenantId = tid }; db.Shifts.Add(item); }
        item.ShiftCode = r.ShiftCode.Trim(); item.ShiftName = r.ShiftName.Trim(); item.Description = r.Description?.Trim(); item.ShiftType = r.ShiftType; item.IsDefault = r.IsDefault; item.StartTime = r.StartTime; item.EndTime = r.EndTime; item.CrossesMidnight = r.CrossesMidnight || r.EndTime <= r.StartTime; item.IsNightShift = r.IsNightShift || item.CrossesMidnight; item.PlannedDurationMinutes = GrossMinutes(r.StartTime, r.EndTime) - r.Breaks.Where(x => x.EndTime >= x.StartTime).Sum(x => Minutes(x.EndTime) - Minutes(x.StartTime)); item.BreakDurationMinutes = r.BreakDurationMinutes; item.MandatoryStartTime = r.MandatoryStartTime; item.MandatoryEndTime = r.MandatoryEndTime; item.StretchedStartTime = r.StretchedStartTime; item.StretchedEndTime = r.StretchedEndTime; item.MinimumWorkMinutes = r.MinimumWorkMinutes; item.FullDayWorkMinutes = r.FullDayWorkMinutes; item.HalfDayWorkMinutes = r.HalfDayWorkMinutes; item.GraceInMinutes = r.GraceInMinutes; item.GraceOutMinutes = r.GraceOutMinutes; item.LateThresholdMinutes = r.LateThresholdMinutes; item.EarlyOutThresholdMinutes = r.EarlyOutThresholdMinutes; item.AllowEarlyMarkIn = r.AllowEarlyMarkIn; item.MaximumEarlyMarkInMinutes = r.MaximumEarlyMarkInMinutes; item.PostShiftMarkOutMode = r.PostShiftMarkOutMode; item.MaximumPostShiftMinutes = r.MaximumPostShiftMinutes; item.IsMarkOutMandatory = r.IsMarkOutMandatory; item.AllowPresentOnSinglePunch = r.AllowPresentOnSinglePunch; item.RequireExpectedWorkMinutes = r.RequireExpectedWorkMinutes; item.ShowLateInIndicator = r.ShowLateInIndicator; item.ShowEarlyOutIndicator = r.ShowEarlyOutIndicator; item.UseDefaultAttendanceMethodology = r.UseDefaultAttendanceMethodology; item.AllowedAttendanceSources = r.AllowedAttendanceSources; item.PrimaryAttendanceSource = r.PrimaryAttendanceSource; item.CaptureMode = r.CaptureMode; item.IsActive = r.IsActive; item.EffectiveFrom = r.EffectiveFrom; item.EffectiveTo = r.EffectiveTo;
        if (item.Breaks.Count > 0) db.ShiftBreaks.RemoveRange(item.Breaks); item.Breaks = r.Breaks.Select(x => new ShiftBreak { Id = Guid.NewGuid(), TenantId = tid, ShiftId = item.Id, Name = x.Name.Trim(), StartTime = x.StartTime, EndTime = x.EndTime, Description = x.Description?.Trim(), Sequence = x.Sequence, IsPaid = x.IsPaid }).ToList();
        await db.SaveChangesAsync(ct); return Result<ShiftDto>.Success(ToDto(item), id is null ? "Shift created." : "Shift updated.");
    }

    public async Task<Result<IReadOnlyList<ShiftPatternDto>>> GetPatternsAsync(CancellationToken ct = default)
    { if (!TryTenant(out var tid)) return Result<IReadOnlyList<ShiftPatternDto>>.Unauthorized("No authenticated tenant."); var rows = await db.ShiftPatterns.AsNoTracking().Include(x => x.Days).Where(x => x.TenantId == tid).OrderBy(x => x.Code).ToListAsync(ct); return Result<IReadOnlyList<ShiftPatternDto>>.Success(rows.Select(ToDto).ToList()); }

    public async Task<Result<ShiftPatternDto>> CreatePatternAsync(ShiftPatternRequest r, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<ShiftPatternDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(r.Code) || string.IsNullOrWhiteSpace(r.Name)) return Result<ShiftPatternDto>.Invalid("code", "Code and name are required.");
        if (r.CycleLengthDays < 1 || r.CycleLengthDays > 366 || r.Days.Count != r.CycleLengthDays || r.Days.Select(x => x.SequenceDay).Distinct().Count() != r.CycleLengthDays) return Result<ShiftPatternDto>.Invalid("days", "A pattern must contain one row for every sequence day in its N-day cycle.");
        if (await db.ShiftPatterns.AnyAsync(x => x.TenantId == tid && x.Code == r.Code.Trim(), ct)) return Result<ShiftPatternDto>.Conflict("Pattern code already exists in this tenant.");
        var shiftIds = r.Days.Where(x => x.ShiftId.HasValue).Select(x => x.ShiftId!.Value).Distinct().ToList(); if (await db.Shifts.CountAsync(x => x.TenantId == tid && shiftIds.Contains(x.Id) && x.IsActive, ct) != shiftIds.Count) return Result<ShiftPatternDto>.Invalid("days", "Every selected Shift must be active and belong to this tenant.");
        var p = new ShiftPattern { Id = Guid.NewGuid(), TenantId = tid, Code = r.Code.Trim(), Name = r.Name.Trim(), CycleLengthDays = r.CycleLengthDays, IsActive = r.IsActive, EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo, Days = r.Days.OrderBy(x => x.SequenceDay).Select(x => new ShiftPatternDay { Id = Guid.NewGuid(), TenantId = tid, SequenceDay = x.SequenceDay, ShiftId = x.ShiftId, DayType = x.DayType }).ToList() }; db.ShiftPatterns.Add(p); await db.SaveChangesAsync(ct); return Result<ShiftPatternDto>.Success(ToDto(p), "Shift pattern created.");
    }

    public async Task<Result<ShiftApplicabilityRequest>> AddApplicabilityAsync(ShiftApplicabilityRequest r, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<ShiftApplicabilityRequest>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(r.RuleName)) return Result<ShiftApplicabilityRequest>.Invalid("ruleName", "Rule name is required.");
        r.RuleName = r.RuleName.Trim();
        if (r.RuleName.Length > 200) return Result<ShiftApplicabilityRequest>.Invalid("ruleName", "Rule name cannot exceed 200 characters.");
        if ((r.ShiftId is null) == (r.ShiftPatternId is null)) return Result<ShiftApplicabilityRequest>.Invalid("shiftId", "Specify exactly one Shift or Shift Pattern.");
        if (r.EffectiveTo < r.EffectiveFrom) return Result<ShiftApplicabilityRequest>.Invalid("effectiveTo", "EffectiveTo cannot be before EffectiveFrom.");
        if (periodLock is not null && !(await periodLock.EnsureRangeIsOpenAsync(r.EffectiveFrom, r.EffectiveTo ?? DateOnly.MaxValue, ct)).Succeeded) return Result<ShiftApplicabilityRequest>.Conflict("The effective applicability interval intersects a closed Attendance period.");
        if (r.ShiftId is Guid sid && !await db.Shifts.AnyAsync(x => x.TenantId == tid && x.Id == sid && x.IsActive, ct)) return Result<ShiftApplicabilityRequest>.Invalid("shiftId", "Shift was not found in this tenant.");
        if (r.ShiftPatternId is Guid pid && !await db.ShiftPatterns.AnyAsync(x => x.TenantId == tid && x.Id == pid && x.IsActive, ct)) return Result<ShiftApplicabilityRequest>.Invalid("shiftPatternId", "Shift Pattern was not found in this tenant.");
        if (r.EmployeeId is Guid employeeId && !await db.Employees.AnyAsync(x => x.TenantId == tid && x.Id == employeeId, ct)) return Result<ShiftApplicabilityRequest>.Invalid("employeeId", "Employee was not found in this tenant.");
        var item = new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = tid, RuleName = r.RuleName, EmployeeId = r.EmployeeId, ShiftId = r.ShiftId, ShiftPatternId = r.ShiftPatternId, Priority = r.Priority, EffectiveFrom = r.EffectiveFrom, EffectiveTo = r.EffectiveTo, Gender = r.Gender, HoldingCompanyId = r.HoldingCompanyId, LobId = r.LobId, OrganisationId = r.OrganisationId, DepartmentId = r.DepartmentId, SubDepartmentId = r.SubDepartmentId, SectionId = r.SectionId, SubSectionId = r.SubSectionId, FunctionId = r.FunctionId, SubFunctionId = r.SubFunctionId, GradeId = r.GradeId, DesignationId = r.DesignationId, EmployeeTypeId = r.EmployeeTypeId, CountryLocationId = r.CountryLocationId, WorkLocationId = r.WorkLocationId, CostCenterId = r.CostCenterId };
        db.ShiftApplicabilityRules.Add(item); await db.SaveChangesAsync(ct); return Result<ShiftApplicabilityRequest>.Success(r, "Applicability rule created.");
    }

    public async Task<Result<PagedResult<ShiftApplicabilityDto>>> GetApplicabilityAsync(ShiftApplicabilityQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<PagedResult<ShiftApplicabilityDto>>.Unauthorized("No authenticated tenant.");
        var source = db.ShiftApplicabilityRules.AsNoTracking().Where(x => x.TenantId == tid).OrderByDescending(x => x.Priority).ThenBy(x => x.EffectiveFrom);
        var page = await source.ToPagedResultAsync(query, ct);
        return Result<PagedResult<ShiftApplicabilityDto>>.Success(new(page.Items.Select(ToApplicabilityDto).ToList(), page.Page, page.PageSize, page.TotalCount));
    }

    public async Task<Result<ShiftApplicabilityDto>> GetApplicabilityByIdAsync(Guid id, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<ShiftApplicabilityDto>.Unauthorized("No authenticated tenant.");
        var item = await db.ShiftApplicabilityRules.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        return item is null ? Result<ShiftApplicabilityDto>.NotFound("Applicability rule was not found.") : Result<ShiftApplicabilityDto>.Success(ToApplicabilityDto(item));
    }

    public async Task<Result<ShiftApplicabilityDto>> UpdateApplicabilityAsync(Guid id, ShiftApplicabilityRequest r, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<ShiftApplicabilityDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(r.RuleName)) return Result<ShiftApplicabilityDto>.Invalid("ruleName", "Rule name is required.");
        r.RuleName = r.RuleName.Trim();
        if (r.RuleName.Length > 200) return Result<ShiftApplicabilityDto>.Invalid("ruleName", "Rule name cannot exceed 200 characters.");
        if ((r.ShiftId is null) == (r.ShiftPatternId is null)) return Result<ShiftApplicabilityDto>.Invalid("target", "Specify exactly one Shift or Shift Pattern.");
        if (r.EffectiveTo < r.EffectiveFrom) return Result<ShiftApplicabilityDto>.Invalid("effectiveTo", "EffectiveTo cannot be before EffectiveFrom.");
        if (periodLock is not null && !(await periodLock.EnsureRangeIsOpenAsync(r.EffectiveFrom, r.EffectiveTo ?? DateOnly.MaxValue, ct)).Succeeded) return Result<ShiftApplicabilityDto>.Conflict("The effective applicability interval intersects a closed Attendance period.");
        var item = await db.ShiftApplicabilityRules.SingleOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        if (item is null) return Result<ShiftApplicabilityDto>.NotFound("Applicability rule was not found.");
        item.RuleName = r.RuleName; item.ShiftId = r.ShiftId; item.ShiftPatternId = r.ShiftPatternId; item.EmployeeId = r.EmployeeId; item.Priority = r.Priority; item.EffectiveFrom = r.EffectiveFrom; item.EffectiveTo = r.EffectiveTo;
        item.Gender = r.Gender; item.HoldingCompanyId = r.HoldingCompanyId; item.LobId = r.LobId; item.OrganisationId = r.OrganisationId; item.DepartmentId = r.DepartmentId; item.SubDepartmentId = r.SubDepartmentId; item.SectionId = r.SectionId; item.SubSectionId = r.SubSectionId; item.FunctionId = r.FunctionId; item.SubFunctionId = r.SubFunctionId; item.GradeId = r.GradeId; item.DesignationId = r.DesignationId; item.EmployeeTypeId = r.EmployeeTypeId; item.CountryLocationId = r.CountryLocationId; item.WorkLocationId = r.WorkLocationId; item.CostCenterId = r.CostCenterId;
        await db.SaveChangesAsync(ct); return Result<ShiftApplicabilityDto>.Success(ToApplicabilityDto(item), "Applicability rule updated.");
    }

    public async Task<Result<bool>> DeleteApplicabilityAsync(Guid id, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<bool>.Unauthorized("No authenticated tenant.");
        var item = await db.ShiftApplicabilityRules.SingleOrDefaultAsync(x => x.TenantId == tid && x.Id == id, ct);
        if (item is null) return Result<bool>.NotFound("Applicability rule was not found.");
        db.ShiftApplicabilityRules.Remove(item); await db.SaveChangesAsync(ct); return Result<bool>.Success(true, "Applicability rule deleted.");
    }

    public async Task<Result<ShiftResolutionDto>> ResolveAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<ShiftResolutionDto>.Unauthorized("No authenticated tenant.");
        var employment = await employmentResolver.ResolveAsync(tid, employeeId, date, ct); if (employment.Status != EffectiveEmploymentResolutionStatus.Resolved || employment.Employment is null) return Result<ShiftResolutionDto>.NotFound(employment.Message);
        var manual = await db.EmployeeRosterDays.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.EmployeeId == employeeId && x.RosterDate == date, ct);
        if (manual is not null) return Result<ShiftResolutionDto>.Success(new(employeeId, date, manual.ShiftId, manual.ShiftPatternId, null, manual.AssignmentSource, manual.IsCalendarOverride, "Explicit roster assignment overrides automatic applicability."));
        var h = employment.Employment; var rules = await db.ShiftApplicabilityRules.AsNoTracking().Where(x => x.TenantId == tid && x.EffectiveFrom <= date && (x.EffectiveTo == null || date <= x.EffectiveTo)).ToListAsync(ct); var matches = rules.Where(x => Matches(x, employeeId, h)).Select(x => new { Rule = x, Specificity = Specificity(x) }).ToList(); if (matches.Count == 0) { var defaults = await db.Shifts.AsNoTracking().Where(x => x.TenantId == tid && x.IsActive && x.IsDefault && x.EffectiveFrom <= date && (x.EffectiveTo == null || date <= x.EffectiveTo)).ToListAsync(ct); if (defaults.Count == 1) return Result<ShiftResolutionDto>.Success(new(employeeId, date, defaults[0].Id, null, null, RosterAssignmentSource.System, false, "Tenant default Shift fallback resolved.")); if (defaults.Count > 1) return Result<ShiftResolutionDto>.Conflict("Multiple active default Shifts apply on this date."); var calendarType = await CalendarDayTypeAsync(employeeId, date, ct); if (calendarType == RosterCalendarDayType.Holiday) return Result<ShiftResolutionDto>.Success(new(employeeId, date, null, null, null, RosterAssignmentSource.System, false, "Holiday: no explicit roster assignment applies.")); if (calendarType == RosterCalendarDayType.WeeklyOff) return Result<ShiftResolutionDto>.Success(new(employeeId, date, null, null, null, RosterAssignmentSource.System, false, "WeeklyOff: no explicit roster assignment applies.")); return Result<ShiftResolutionDto>.Success(new(employeeId, date, null, null, null, RosterAssignmentSource.Auto, false, "NotConfigured: no applicable Shift or Pattern rule was found.")); } var best = matches.Where(x => x.Rule.Priority == matches.Max(y => y.Rule.Priority)).ToList(); var specific = best.Where(x => x.Specificity == best.Max(y => y.Specificity)).ToList(); if (specific.Count != 1) return Result<ShiftResolutionDto>.Conflict("Multiple Shift applicability rules have the same best priority and specificity."); var rule = specific[0].Rule; if (rule.ShiftId is Guid shift) return Result<ShiftResolutionDto>.Success(new(employeeId, date, shift, null, null, RosterAssignmentSource.Auto, false, "Automatic Shift applicability resolved.")); var pattern = await db.ShiftPatterns.AsNoTracking().Include(x => x.Days).SingleAsync(x => x.TenantId == tid && x.Id == rule.ShiftPatternId, ct); var offset = (date.DayNumber - pattern.EffectiveFrom.DayNumber) % pattern.CycleLengthDays; if (offset < 0) offset += pattern.CycleLengthDays; var day = pattern.Days.Single(x => x.SequenceDay == offset + 1); return Result<ShiftResolutionDto>.Success(new(employeeId, date, day.ShiftId, pattern.Id, day.DayType, RosterAssignmentSource.Auto, false, "Automatic Shift Pattern applicability resolved."));
    }

    public async Task<Result<IReadOnlyList<RosterDayDto>>> AssignRosterAsync(RosterAssignmentRequest r, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<IReadOnlyList<RosterDayDto>>.Unauthorized("No authenticated tenant.");
        if (r.FromDate > r.ToDate || r.EmployeeIds.Count == 0) return Result<IReadOnlyList<RosterDayDto>>.Invalid("dateRange", "A valid employee list and date range are required.");
        if (periodLock is not null && !(await periodLock.EnsureRangeIsOpenAsync(r.FromDate, r.ToDate, ct)).Succeeded) return Result<IReadOnlyList<RosterDayDto>>.Conflict("The Attendance period is closed and must be reopened before this change.");
        var days = r.ToDate.DayNumber - r.FromDate.DayNumber + 1;
        if ((long)days * r.EmployeeIds.Count > MaxUploadRows) return Result<IReadOnlyList<RosterDayDto>>.Invalid("dateRange", $"The request exceeds the safe limit of {MaxUploadRows:N0} roster rows.");
        if (r.DayType == RosterDayType.Shift && r.ShiftId is null) return Result<IReadOnlyList<RosterDayDto>>.Invalid("shiftId", "Shift is required for a working roster day.");
        if (r.ShiftId is Guid sid && !await db.Shifts.AnyAsync(x => x.TenantId == tid && x.Id == sid && x.IsActive, ct)) return Result<IReadOnlyList<RosterDayDto>>.Invalid("shiftId", "Shift was not found in this tenant.");
        var result = new List<EmployeeRosterDay>();
        foreach (var employeeId in r.EmployeeIds.Distinct())
        {
            if (!await db.Employees.AnyAsync(x => x.TenantId == tid && x.Id == employeeId, ct)) return Result<IReadOnlyList<RosterDayDto>>.Invalid("employeeIds", "One or more employees were not found in this tenant.");
            for (var d = r.FromDate; d <= r.ToDate; d = d.AddDays(1))
            {
                var originalCalendarDayType = await CalendarDayTypeAsync(employeeId, d, ct);
                var item = await db.EmployeeRosterDays.FirstOrDefaultAsync(x => x.TenantId == tid && x.EmployeeId == employeeId && x.RosterDate == d, ct);
                var wasNew = item is null;
                var previousShift = item?.ShiftId; var previousDayType = item?.DayType ?? RosterDayType.NonWorking;
                if (item is not null && previousShift == r.ShiftId && previousDayType == r.DayType && item.AssignmentSource == RosterAssignmentSource.Manual && item.OriginalCalendarDayType == originalCalendarDayType) continue;
                if (item is null) { item = new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = tid, EmployeeId = employeeId, RosterDate = d }; db.EmployeeRosterDays.Add(item); }
                var isCalendarOverride = r.DayType == RosterDayType.WeeklyOff || (originalCalendarDayType is RosterCalendarDayType.Holiday or RosterCalendarDayType.WeeklyOff && r.DayType == RosterDayType.Shift);
                db.EmployeeRosterChangeHistories.Add(NewHistory(tid, employeeId, d, wasNew ? null : item, r.ShiftId, r.DayType, RosterAssignmentSource.Manual, isCalendarOverride, originalCalendarDayType, r.Reason, null, wasNew ? RosterChangeType.Created : RosterChangeType.Updated));
                item.OriginalShiftId ??= item.ShiftId; item.ShiftId = r.ShiftId; item.DayType = r.DayType; item.AssignmentSource = RosterAssignmentSource.Manual; item.IsOverride = true; item.IsCalendarOverride = isCalendarOverride; item.OriginalCalendarDayType = originalCalendarDayType; item.Comment = r.Reason; result.Add(item);
            }
        }
        await db.SaveChangesAsync(ct); return Result<IReadOnlyList<RosterDayDto>>.Success(result.Select(ToDto).ToList(), "Roster assigned.");
    }

    public async Task<Result<bool>> RemoveRosterAsync(Guid employeeId, DateOnly date, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<bool>.Unauthorized("No authenticated tenant.");
        if (periodLock is not null && !(await periodLock.EnsureDateIsOpenAsync(date, ct)).Succeeded) return Result<bool>.Conflict("The Attendance period is closed and must be reopened before this change.");
        var item = await db.EmployeeRosterDays.FirstOrDefaultAsync(x => x.TenantId == tid && x.EmployeeId == employeeId && x.RosterDate == date, ct);
        if (item is null) return Result<bool>.NotFound("Roster assignment was not found.");
        db.EmployeeRosterChangeHistories.Add(NewHistory(tid, employeeId, date, item, null, RosterDayType.NonWorking, RosterAssignmentSource.Auto, false, item.OriginalCalendarDayType, item.Comment, null, RosterChangeType.Removed));
        db.EmployeeRosterDays.Remove(item); await db.SaveChangesAsync(ct); return Result<bool>.Success(true, "Roster override removed; calendar/automatic resolution will apply.");
    }

    public async Task<Result<PagedResult<RosterGridRowDto>>> GetRosterAsync(RosterQuery query, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<PagedResult<RosterGridRowDto>>.Unauthorized("No authenticated tenant.");
        if (query.FromDate is not DateOnly from || query.ToDate is not DateOnly to) return Result<PagedResult<RosterGridRowDto>>.Invalid("dateRange", "FromDate and ToDate are required for roster queries.");
        if (from > to) return Result<PagedResult<RosterGridRowDto>>.Invalid("dateRange", "FromDate cannot be after ToDate.");
        if (to.DayNumber - from.DayNumber + 1 > 366) return Result<PagedResult<RosterGridRowDto>>.Invalid("dateRange", "Roster query range cannot exceed 366 days.");
        var employees = db.Employees.AsNoTracking().Where(x => x.TenantId == tid);
        if (query.EmployeeId is Guid employeeId) employees = employees.Where(x => x.Id == employeeId);
        if (!string.IsNullOrWhiteSpace(query.Search)) employees = employees.Where(x => x.EmployeeCode != null && x.EmployeeCode.Contains(query.Search));
        var employeeRows = await employees.OrderBy(x => x.EmployeeCode).ThenBy(x => x.Id).ToListAsync(ct);
        var explicitRows = await db.EmployeeRosterDays.AsNoTracking().Where(x => x.TenantId == tid && x.RosterDate >= from && x.RosterDate <= to).ToListAsync(ct);
        var shifts = await db.Shifts.AsNoTracking().Where(x => x.TenantId == tid).ToDictionaryAsync(x => x.Id, ct);
        var resolved = new List<RosterGridRowDto>();
        foreach (var employee in employeeRows)
        {
            for (var date = from; date <= to; date = date.AddDays(1))
            {
                var resolution = await ResolveAsync(employee.Id, date, ct);
                if (!resolution.Succeeded || resolution.Value is null) continue;
                var value = resolution.Value; var explicitRow = explicitRows.SingleOrDefault(x => x.EmployeeId == employee.Id && x.RosterDate == date); var underlyingCalendar = explicitRow?.OriginalCalendarDayType ?? await CalendarDayTypeAsync(employee.Id, date, ct);
                if (explicitRow is null && underlyingCalendar == RosterCalendarDayType.Holiday && value.Source == RosterAssignmentSource.System) value = new(value.EmployeeId, value.Date, null, null, null, value.Source, false, "Holiday: no explicit roster assignment applies.");
                var dayType = explicitRow?.DayType ?? (value.PatternDayType == ShiftPatternDayType.WeeklyOff || value.Message.StartsWith("WeeklyOff", StringComparison.OrdinalIgnoreCase) ? RosterDayType.WeeklyOff : value.Message.StartsWith("Holiday", StringComparison.OrdinalIgnoreCase) ? RosterDayType.Holiday : RosterDayType.Shift);
                var shift = dayType == RosterDayType.WeeklyOff || dayType == RosterDayType.Holiday ? null : value.ShiftId is Guid shiftId && shifts.TryGetValue(shiftId, out var found) ? found : null;
                if (query.ShiftId is Guid filterShift && shift?.Id != filterShift) continue;
                if (query.DayType is RosterDayType filterDay && dayType != filterDay) continue;
                if (query.AssignmentSource is RosterAssignmentSource filterSource && value.Source != filterSource) continue;
                resolved.Add(new(employee.Id, employee.EmployeeCode ?? string.Empty, $"{employee.FirstName} {employee.LastName}".Trim(), date, dayType, shift?.Id, shift?.ShiftCode, shift?.ShiftName, value.Source, explicitRow is not null, explicitRow?.IsCalendarOverride ?? value.IsCalendarOverride, underlyingCalendar, explicitRow is not null));
            }
        }
        var ordered = resolved.OrderBy(x => x.RosterDate).ThenBy(x => x.EmployeeCode).ThenBy(x => x.EmployeeId); var total = resolved.Count; var pageSize = Math.Clamp(query.PageSize, 1, PagedQuery.MaxPageSize); var page = Math.Max(1, query.Page);
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Result<PagedResult<RosterGridRowDto>>.Success(new(items, page, pageSize, total));
    }

    public async Task<Result<RosterUploadBatchDto>> ValidateRosterUploadAsync(string fileName, Stream csv, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<RosterUploadBatchDto>.Unauthorized("No authenticated tenant.");
        var batch = new RosterUploadBatch { Id = Guid.NewGuid(), TenantId = tid, FileName = Path.GetFileName(fileName), Status = RosterUploadStatus.Validating };
        db.RosterUploadBatches.Add(batch);
        using var reader = new StreamReader(csv);
        var header = await reader.ReadLineAsync(ct);
        if (header is null || !header.Split(',').Select(x => x.Trim()).SequenceEqual(new[] { "EmployeeCode", "Date", "ShiftCode", "DayType" }, StringComparer.OrdinalIgnoreCase)) return Result<RosterUploadBatchDto>.Invalid("file", "CSV headers must be EmployeeCode,Date,ShiftCode,DayType.");
        var seen = new Dictionary<string, RosterUploadRow>(StringComparer.OrdinalIgnoreCase);
        string? line; var rowNo = 1;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            if (++rowNo > MaxUploadRows + 1) return Result<RosterUploadBatchDto>.Invalid("file", $"The file exceeds the safe limit of {MaxUploadRows:N0} rows.");
            if (string.IsNullOrWhiteSpace(line)) continue;
            var c = line.Split(',');
            var uploadedShiftCode = c.ElementAtOrDefault(2)?.Trim();
            var row = new RosterUploadRow { Id = Guid.NewGuid(), TenantId = tid, RosterUploadBatchId = batch.Id, RowNumber = rowNo, EmployeeCode = c.ElementAtOrDefault(0)?.Trim() ?? string.Empty, ShiftCode = string.IsNullOrWhiteSpace(uploadedShiftCode) ? null : uploadedShiftCode, DayType = ParseDayType(c.ElementAtOrDefault(3)), IsValid = true };
            batch.Rows.Add(row); batch.TotalRows++;
            if (!DateOnly.TryParse(c.ElementAtOrDefault(1), out var date)) { row.IsValid = false; row.ErrorMessage = "Date is invalid."; } else row.RosterDate = date;
            if (row.DayType == RosterDayType.NonWorking && !string.IsNullOrWhiteSpace(row.ShiftCode)) { row.IsValid = false; row.ErrorMessage = "ShiftCode must be empty for NonWorking."; }
            if (!Enum.TryParse<RosterDayType>(c.ElementAtOrDefault(3), true, out _)) { row.IsValid = false; row.ErrorMessage = "DayType is invalid."; }
            var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.EmployeeCode == row.EmployeeCode, ct);
            if (employee is null) { row.IsValid = false; row.ErrorMessage = "EmployeeCode was not found in this tenant."; }
            Shift? shift = null;
            if (!string.IsNullOrWhiteSpace(row.ShiftCode)) shift = await db.Shifts.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.ShiftCode == row.ShiftCode && x.IsActive, ct);
            if (row.DayType == RosterDayType.Shift && shift is null) { row.IsValid = false; row.ErrorMessage = "ShiftCode was not found or is inactive."; }
            if (row.IsValid && employee is not null)
            {
                row.UnderlyingCalendarDayType = await CalendarDayTypeAsync(employee.Id, row.RosterDate, ct);
                var current = await db.EmployeeRosterDays.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tid && x.EmployeeId == employee.Id && x.RosterDate == row.RosterDate, ct);
                row.Action = current is null ? RosterUploadAction.New : current.ShiftId == shift?.Id && current.DayType == row.DayType ? RosterUploadAction.Unchanged : RosterUploadAction.Update;
                row.IsCurrentCalendarOverride = current?.IsCalendarOverride ?? false;
                row.WillOverrideCalendar = row.DayType == RosterDayType.WeeklyOff || (row.UnderlyingCalendarDayType is RosterCalendarDayType.Holiday or RosterCalendarDayType.WeeklyOff && row.DayType == RosterDayType.Shift);
                row.CurrentShiftCode = current?.ShiftId is Guid currentShiftId ? await db.Shifts.AsNoTracking().Where(x => x.TenantId == tid && x.Id == currentShiftId).Select(x => x.ShiftCode).SingleOrDefaultAsync(ct) : null; row.CurrentDayType = current?.DayType;
            }
            var key = $"{row.EmployeeCode}|{row.RosterDate:yyyy-MM-dd}";
            if (seen.TryGetValue(key, out var prior))
            {
                if (prior.IsValid) { prior.IsValid = false; prior.Action = RosterUploadAction.Error; prior.ErrorMessage = "Duplicate employee/date row."; batch.ValidRows--; batch.InvalidRows++; }
                row.IsValid = false; row.Action = RosterUploadAction.Error; row.ErrorMessage = "Duplicate employee/date row.";
            }
            else seen[key] = row;
            if (row.IsValid) batch.ValidRows++; else { row.Action = RosterUploadAction.Error; batch.InvalidRows++; }
        }
        batch.Status = batch.InvalidRows == 0 ? RosterUploadStatus.Validated : RosterUploadStatus.Failed;
        await db.SaveChangesAsync(ct);
        return Result<RosterUploadBatchDto>.Success(ToDto(batch), batch.Status == RosterUploadStatus.Validated ? "Roster upload validated." : "Roster upload contains validation errors.");
    }

    public async Task<Result<RosterUploadBatchDto>> CommitRosterUploadAsync(Guid batchId, CancellationToken ct = default)
    {
        if (!TryTenant(out var tid)) return Result<RosterUploadBatchDto>.Unauthorized("No authenticated tenant.");
        var batch = await db.RosterUploadBatches.Include(x => x.Rows).FirstOrDefaultAsync(x => x.TenantId == tid && x.Id == batchId, ct);
        if (batch is null) return Result<RosterUploadBatchDto>.NotFound("Roster upload batch was not found.");
        if (batch.Status != RosterUploadStatus.Validated) return Result<RosterUploadBatchDto>.Conflict("Only a validated roster upload can be committed.");
        foreach (var row in batch.Rows.Where(x => x.IsValid))
        {
            var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.TenantId == tid && x.EmployeeCode == row.EmployeeCode, ct);
            var shift = string.IsNullOrWhiteSpace(row.ShiftCode) ? null : await db.Shifts.AsNoTracking().SingleAsync(x => x.TenantId == tid && x.ShiftCode == row.ShiftCode, ct);
            var item = await db.EmployeeRosterDays.FirstOrDefaultAsync(x => x.TenantId == tid && x.EmployeeId == employee.Id && x.RosterDate == row.RosterDate, ct);
            if (row.Action == RosterUploadAction.Unchanged || (item is not null && item.ShiftId == shift?.Id && item.DayType == row.DayType)) continue;
            var previous = item;
            if (item is null) { item = new EmployeeRosterDay { Id = Guid.NewGuid(), TenantId = tid, EmployeeId = employee.Id, RosterDate = row.RosterDate }; db.EmployeeRosterDays.Add(item); }
            var originalCalendarDayType = await CalendarDayTypeAsync(employee.Id, row.RosterDate, ct);
            var isCalendarOverride = row.DayType == RosterDayType.WeeklyOff || (originalCalendarDayType is RosterCalendarDayType.Holiday or RosterCalendarDayType.WeeklyOff && row.DayType == RosterDayType.Shift);
            db.EmployeeRosterChangeHistories.Add(NewHistory(tid, employee.Id, row.RosterDate, item, shift?.Id, row.DayType, RosterAssignmentSource.Upload, isCalendarOverride, originalCalendarDayType, null, batch.Id, previous is null ? RosterChangeType.Created : RosterChangeType.Updated));
            item.ShiftId = shift?.Id; item.DayType = row.DayType; item.AssignmentSource = RosterAssignmentSource.Upload; item.IsOverride = true; item.IsCalendarOverride = isCalendarOverride; item.OriginalCalendarDayType = originalCalendarDayType; batch.CommittedRows++;
        }
        batch.Status = RosterUploadStatus.Committed; await db.SaveChangesAsync(ct); return Result<RosterUploadBatchDto>.Success(ToDto(batch), "Roster upload committed.");
    }

    private static (string field, string message)? ValidateShift(ShiftRequest r) { if (string.IsNullOrWhiteSpace(r.ShiftCode)) return ("shiftCode", "Shift code is required."); if (string.IsNullOrWhiteSpace(r.ShiftName)) return ("shiftName", "Shift name is required."); if (r.EffectiveTo < r.EffectiveFrom) return ("effectiveTo", "EffectiveTo cannot be before EffectiveFrom."); if (r.BreakDurationMinutes < 0 || r.MinimumWorkMinutes < 0 || r.FullDayWorkMinutes < 0 || r.GraceInMinutes < 0 || r.GraceOutMinutes < 0 || r.MaximumEarlyMarkInMinutes < 0 || r.MaximumPostShiftMinutes < 0) return ("minutes", "Minute values cannot be negative."); if (r.MandatoryStartTime is not null && r.MandatoryEndTime is not null && r.MandatoryEndTime <= r.MandatoryStartTime) return ("mandatoryWindow", "Mandatory attendance window is invalid."); return null; }
    private static (string field, string message)? ValidateCaptureSources(ShiftRequest r)
    {
        if (!r.UseDefaultAttendanceMethodology && r.AllowedAttendanceSources == 0) return ("allowedAttendanceSources", "At least one attendance source is required when methodology override is enabled.");
        if (r.PrimaryAttendanceSource.HasValue && (r.AllowedAttendanceSources & r.PrimaryAttendanceSource.Value) != r.PrimaryAttendanceSource.Value) return ("primaryAttendanceSource", "Primary attendance source must be one of the allowed sources.");
        var legacy = r.CaptureMode switch { AttendanceCaptureMode.BiometricOnly => AttendanceSource.Biometric, AttendanceCaptureMode.SelfPunch => AttendanceSource.Portal, AttendanceCaptureMode.AutoLoginLogout => AttendanceSource.AutoLoginLogout, AttendanceCaptureMode.Manual => AttendanceSource.Manual, _ => (AttendanceSource)0 };
        if (!r.UseDefaultAttendanceMethodology && r.CaptureMode != AttendanceCaptureMode.Mixed && r.AllowedAttendanceSources != legacy) return ("allowedAttendanceSources", "Legacy CaptureMode conflicts with the selected attendance sources.");
        return null;
    }
    private static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;
    private static int GrossMinutes(TimeOnly start, TimeOnly end) { var result = Minutes(end) - Minutes(start); return result > 0 ? result : result + 1440; }
    private static bool TokenMatches(Shift x, string? token) => !string.IsNullOrWhiteSpace(token) && token == (x.ModifiedDate ?? x.CreatedDate).ToString("O");
    private static ShiftApplicabilityDto ToApplicabilityDto(ShiftApplicabilityRule x) => new(x.Id, x.RuleName, x.Priority, x.EffectiveFrom, x.EffectiveTo, x.ShiftId, x.ShiftPatternId, true, new Dictionary<string, Guid?> { ["Employee"] = x.EmployeeId, ["Department"] = x.DepartmentId, ["WorkLocation"] = x.WorkLocationId, ["EmployeeType"] = x.EmployeeTypeId, ["Grade"] = x.GradeId, ["Designation"] = x.DesignationId, ["CostCenter"] = x.CostCenterId });
    private static ShiftDto ToDto(Shift x) => new(x.Id, x.ShiftCode, x.ShiftName, x.Description, x.ShiftType, x.IsDefault, x.StartTime, x.EndTime, x.PlannedDurationMinutes, x.BreakDurationMinutes, x.MandatoryStartTime, x.MandatoryEndTime, x.StretchedStartTime, x.StretchedEndTime, x.MinimumWorkMinutes, x.FullDayWorkMinutes, x.HalfDayWorkMinutes, x.GraceInMinutes, x.GraceOutMinutes, x.LateThresholdMinutes, x.EarlyOutThresholdMinutes, x.IsNightShift, x.CrossesMidnight, x.CaptureMode, x.AllowedAttendanceSources, x.PrimaryAttendanceSource, x.UseDefaultAttendanceMethodology, x.AllowEarlyMarkIn, x.MaximumEarlyMarkInMinutes, x.PostShiftMarkOutMode, x.MaximumPostShiftMinutes, x.IsMarkOutMandatory, x.AllowPresentOnSinglePunch, x.RequireExpectedWorkMinutes, x.ShowLateInIndicator, x.ShowEarlyOutIndicator, x.IsActive, x.EffectiveFrom, x.EffectiveTo, x.CreatedDate, x.ModifiedDate, (x.ModifiedDate ?? x.CreatedDate).ToString("O"), x.Breaks.OrderBy(b => b.Sequence).Select(b => new ShiftBreakDto(b.Id, b.Name, b.StartTime, b.EndTime, b.Description, b.Sequence, b.IsPaid)).ToList());
    private static ShiftPatternDto ToDto(ShiftPattern x) => new(x.Id, x.Code, x.Name, x.CycleLengthDays, x.IsActive, x.EffectiveFrom, x.EffectiveTo, x.Days.OrderBy(d => d.SequenceDay).Select(d => new ShiftPatternDayDto(d.SequenceDay, d.ShiftId, d.DayType)).ToList());
    private static RosterDayDto ToDto(EmployeeRosterDay x) => new(x.Id, x.EmployeeId, x.RosterDate, x.ShiftId, x.ShiftPatternId, x.DayType, x.AssignmentSource, x.IsOverride, x.IsCalendarOverride, x.Comment);
    private async Task<RosterCalendarDayType> CalendarDayTypeAsync(Guid employeeId, DateOnly date, CancellationToken ct)
    {
        if (calendarResolver is null) return RosterCalendarDayType.Unknown;
        var result = await calendarResolver.CalculateAsync(employeeId, date, date, ct);
        var detail = result.Value?.Days.SingleOrDefault();
        return detail?.ExclusionReason switch
        {
            WorkingDayExclusionReason.Holiday => RosterCalendarDayType.Holiday,
            WorkingDayExclusionReason.WeeklyOff => RosterCalendarDayType.WeeklyOff,
            _ => RosterCalendarDayType.WorkingDay
        };
    }

    private EmployeeRosterChangeHistory NewHistory(Guid tenantId, Guid employeeId, DateOnly date, EmployeeRosterDay? previous, Guid? newShiftId, RosterDayType newDayType, RosterAssignmentSource newSource, bool newCalendarOverride, RosterCalendarDayType originalCalendarDayType, string? reason, Guid? uploadBatchId, RosterChangeType changeType) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, RosterDate = date,
        PreviousShiftId = previous?.ShiftId, NewShiftId = newShiftId,
        PreviousDayType = previous?.DayType ?? RosterDayType.NonWorking, NewDayType = newDayType,
        Source = newSource, PreviousSource = previous?.AssignmentSource ?? RosterAssignmentSource.Auto, NewSource = newSource,
        ChangeType = changeType, OriginalCalendarDayType = originalCalendarDayType,
        PreviousIsCalendarOverride = previous?.IsCalendarOverride ?? false, NewIsCalendarOverride = newCalendarOverride,
        Reason = reason, UploadBatchId = uploadBatchId, ChangedBy = tenant.UserId?.ToString(), ChangedByUserId = tenant.UserId,
        ChangedAtUtc = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime
    };

    private static RosterUploadBatchDto ToDto(RosterUploadBatch x) => new(x.Id, x.FileName, x.Status, x.TotalRows, x.ValidRows, x.InvalidRows, x.CommittedRows, x.Rows.OrderBy(r => r.RowNumber).Select(r => new RosterUploadRowDto(r.RowNumber, r.EmployeeCode, r.RosterDate, r.ShiftCode, r.DayType, r.IsValid, r.Action, r.CurrentShiftCode, r.CurrentDayType, r.ErrorMessage, r.UnderlyingCalendarDayType, r.IsCurrentCalendarOverride, r.WillOverrideCalendar)).ToList());
    private static RosterDayType ParseDayType(string? value) => Enum.TryParse<RosterDayType>(value, true, out var result) ? result : RosterDayType.NonWorking;
    private static bool Matches(ShiftApplicabilityRule s, EffectiveEmploymentSnapshot h) => (!s.Gender.HasValue || s.Gender == h.Gender) && (!s.HoldingCompanyId.HasValue || s.HoldingCompanyId == h.HoldingCompanyId) && (!s.LobId.HasValue || s.LobId == h.LobId) && (!s.OrganisationId.HasValue || s.OrganisationId == h.OrganisationId) && (!s.DepartmentId.HasValue || s.DepartmentId == h.DepartmentId) && (!s.SubDepartmentId.HasValue || s.SubDepartmentId == h.SubDepartmentId) && (!s.SectionId.HasValue || s.SectionId == h.SectionId) && (!s.SubSectionId.HasValue || s.SubSectionId == h.SubSectionId) && (!s.FunctionId.HasValue || s.FunctionId == h.FunctionId) && (!s.SubFunctionId.HasValue || s.SubFunctionId == h.SubFunctionId) && (!s.GradeId.HasValue || s.GradeId == h.GradeId) && (!s.DesignationId.HasValue || s.DesignationId == h.DesignationId) && (!s.EmployeeTypeId.HasValue || s.EmployeeTypeId == h.EmployeeTypeId) && (!s.CountryLocationId.HasValue || s.CountryLocationId == h.CountryLocationId) && (!s.WorkLocationId.HasValue || s.WorkLocationId == h.WorkLocationId) && (!s.CostCenterId.HasValue || s.CostCenterId == h.CostCenterId);
    private static bool Matches(ShiftApplicabilityRule s, Guid employeeId, EffectiveEmploymentSnapshot h) => (!s.EmployeeId.HasValue || s.EmployeeId == employeeId) && Matches(s, h);
    private static int Specificity(ShiftApplicabilityRule s) => (s.EmployeeId.HasValue ? 100 : 0) + (s.Gender.HasValue ? 1 : 0) + new Guid?[] { s.HoldingCompanyId, s.LobId, s.OrganisationId, s.DepartmentId, s.SubDepartmentId, s.SectionId, s.SubSectionId, s.FunctionId, s.SubFunctionId, s.GradeId, s.DesignationId, s.EmployeeTypeId, s.CountryLocationId, s.WorkLocationId, s.CostCenterId }.Count(x => x.HasValue);
}
