using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

public sealed class AttendanceWorkflowService(
    IHrmsDbContext db,
    IEmployeeIdentityResolver identity,
    IEmployeeManagerResolver managers,
    IAttendanceDayProcessor processor,
    TimeProvider? timeProvider = null,
    IAttendancePeriodLockService? periodLock = null) : IAttendanceWorkflowService
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async Task<Result<RegularizationDto>> SubmitRegularizationAsync(RegularizationRequestInput input, CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded) return Fail<RegularizationDto>(subject);
        if (periodLock is not null && !(await periodLock.EnsureDateIsOpenAsync(input.BusinessDate, ct)).Succeeded) return Result<RegularizationDto>.Conflict("The Attendance period is closed and must be reopened before this change.");
        if (input.BusinessDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)) return Result<RegularizationDto>.Invalid("businessDate", "A future attendance date cannot be regularized.");
        if (string.IsNullOrWhiteSpace(input.Reason)) return Result<RegularizationDto>.Invalid("reason", "A reason is required.");
        if (input.ProposedInAtUtc is null && input.ProposedOutAtUtc is null) return Result<RegularizationDto>.Invalid("punches", "At least one corrected punch is required.");
        var existing = await db.EmployeeAttendanceDays.AsNoTracking().SingleOrDefaultAsync(x => x.EmployeeId == subject.Value!.EmployeeId && x.BusinessDate == input.BusinessDate, ct);
        if (existing is not null && existing.Status == EmployeeAttendanceDayStatus.Present && !existing.HasMissingInPunch && !existing.HasMissingOutPunch && !existing.HasInvalidPunchSequence) return Result<RegularizationDto>.Conflict("A clean Present day is not eligible for regularization.");
        if (await db.AttendanceRegularizationRequests.AnyAsync(x => x.EmployeeId == subject.Value.EmployeeId && x.BusinessDate == input.BusinessDate && x.Status == AttendanceRequestStatus.Pending, ct)) return Result<RegularizationDto>.Conflict("A pending regularization already exists for this date.");
        var now = clock.GetUtcNow().UtcDateTime; var item = new AttendanceRegularizationRequest { Id = Guid.NewGuid(), TenantId = subject.Value.TenantId, EmployeeId = subject.Value.EmployeeId, BusinessDate = input.BusinessDate, RequestType = input.RequestType, ProposedInAtUtc = input.ProposedInAtUtc, ProposedOutAtUtc = input.ProposedOutAtUtc, Reason = input.Reason.Trim(), SubmittedByUserId = subject.Value.UserId, SubmittedAtUtc = now };
        db.AttendanceRegularizationRequests.Add(item); db.AttendanceRegularizationEvents.Add(Event(item.TenantId, item.Id, AttendanceRequestEventType.Submitted, subject.Value.UserId, now, null)); await db.SaveChangesAsync(ct); return Result<RegularizationDto>.Success(Map(item));
    }

    public async Task<Result<PagedResult<RegularizationDto>>> GetMyRegularizationsAsync(PagedQuery query, CancellationToken ct = default) { var s = await Subject(ct); if (!s.Succeeded) return Fail<PagedResult<RegularizationDto>>(s); var q = db.AttendanceRegularizationRequests.AsNoTracking().Where(x => x.EmployeeId == s.Value!.EmployeeId).OrderByDescending(x => x.SubmittedAtUtc); return Result<PagedResult<RegularizationDto>>.Success(await Page(q, query, ct)); }
    public async Task<Result<RegularizationDto>> GetMyRegularizationAsync(Guid id, CancellationToken ct = default) { var s = await Subject(ct); if (!s.Succeeded) return Fail<RegularizationDto>(s); var x = await db.AttendanceRegularizationRequests.Include(x => x.Events).SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == s.Value!.EmployeeId, ct); return x is null ? Result<RegularizationDto>.NotFound("Regularization request was not found.") : Result<RegularizationDto>.Success(Map(x)); }
    public Task<Result<RegularizationDto>> CancelRegularizationAsync(Guid id, CancellationToken ct = default) => TransitionRegularization(id, AttendanceRequestStatus.Cancelled, null, ct);
    public async Task<Result<PagedResult<RegularizationDto>>> GetManagerRegularizationsAsync(PagedQuery query, CancellationToken ct = default) { var s = await Subject(ct); if (!s.Succeeded) return Fail<PagedResult<RegularizationDto>>(s); var all = await db.AttendanceRegularizationRequests.Include(x => x.Events).Where(x => x.Status == AttendanceRequestStatus.Pending).OrderBy(x => x.SubmittedAtUtc).ToListAsync(ct); var allowed = new List<RegularizationDto>(); foreach (var x in all) { var m = await managers.ResolveAsync(x.EmployeeId, x.BusinessDate, ct); if (m.Value?.ManagerId == s.Value!.EmployeeId) allowed.Add(Map(x)); } return Result<PagedResult<RegularizationDto>>.Success(Page(allowed, query)); }
    public Task<Result<RegularizationDto>> ApproveRegularizationAsync(Guid id, CancellationToken ct = default) => TransitionRegularization(id, AttendanceRequestStatus.Approved, null, ct);
    public Task<Result<RegularizationDto>> RejectRegularizationAsync(Guid id, string comments, CancellationToken ct = default) => TransitionRegularization(id, AttendanceRequestStatus.Rejected, comments, ct);

    public async Task<Result<OnDutyDto>> SubmitOnDutyAsync(OnDutyRequestInput input, CancellationToken ct = default)
    {
        var subject = await Subject(ct); if (!subject.Succeeded) return Fail<OnDutyDto>(subject); if (input.StartDate > input.EndDate) return Result<OnDutyDto>.Invalid("dateRange", "StartDate cannot be after EndDate."); if (string.IsNullOrWhiteSpace(input.Reason)) return Result<OnDutyDto>.Invalid("reason", "A reason is required.");
        if (periodLock is not null && !(await periodLock.EnsureRangeIsOpenAsync(input.StartDate, input.EndDate, ct)).Succeeded) return Result<OnDutyDto>.Conflict("The Attendance period is closed and must be reopened before this change.");
        if (await db.AttendanceOnDutyRequests.AnyAsync(x => x.EmployeeId == subject.Value!.EmployeeId && x.Status == AttendanceRequestStatus.Pending && x.StartDate <= input.EndDate && input.StartDate <= x.EndDate, ct)) return Result<OnDutyDto>.Conflict("An overlapping pending On Duty request already exists.");
        var now = clock.GetUtcNow().UtcDateTime; var item = new AttendanceOnDutyRequest { Id = Guid.NewGuid(), TenantId = subject.Value.TenantId, EmployeeId = subject.Value.EmployeeId, StartDate = input.StartDate, EndDate = input.EndDate, Reason = input.Reason.Trim(), Purpose = input.Purpose?.Trim(), Location = input.Location?.Trim(), SubmittedByUserId = subject.Value.UserId, SubmittedAtUtc = now };
        db.AttendanceOnDutyRequests.Add(item); db.AttendanceOnDutyEvents.Add(OnDutyEvent(item.TenantId, item.Id, AttendanceRequestEventType.Submitted, subject.Value.UserId, now, null)); await db.SaveChangesAsync(ct); return Result<OnDutyDto>.Success(Map(item));
    }
    public async Task<Result<PagedResult<OnDutyDto>>> GetMyOnDutyAsync(PagedQuery query, CancellationToken ct = default) { var s = await Subject(ct); if (!s.Succeeded) return Fail<PagedResult<OnDutyDto>>(s); var q = db.AttendanceOnDutyRequests.AsNoTracking().Where(x => x.EmployeeId == s.Value!.EmployeeId).OrderByDescending(x => x.SubmittedAtUtc); return Result<PagedResult<OnDutyDto>>.Success(await Page(q, query, ct)); }
    public async Task<Result<OnDutyDto>> GetMyOnDutyByIdAsync(Guid id, CancellationToken ct = default) { var s = await Subject(ct); if (!s.Succeeded) return Fail<OnDutyDto>(s); var x = await db.AttendanceOnDutyRequests.Include(x => x.Events).SingleOrDefaultAsync(x => x.Id == id && x.EmployeeId == s.Value!.EmployeeId, ct); return x is null ? Result<OnDutyDto>.NotFound("On Duty request was not found.") : Result<OnDutyDto>.Success(Map(x)); }
    public Task<Result<OnDutyDto>> CancelOnDutyAsync(Guid id, CancellationToken ct = default) => TransitionOnDuty(id, AttendanceRequestStatus.Cancelled, null, ct);
    public async Task<Result<PagedResult<OnDutyDto>>> GetManagerOnDutyAsync(PagedQuery query, CancellationToken ct = default) { var s = await Subject(ct); if (!s.Succeeded) return Fail<PagedResult<OnDutyDto>>(s); var all = await db.AttendanceOnDutyRequests.Include(x => x.Events).Where(x => x.Status == AttendanceRequestStatus.Pending).OrderBy(x => x.SubmittedAtUtc).ToListAsync(ct); var allowed = new List<OnDutyDto>(); foreach (var x in all) { var m = await managers.ResolveAsync(x.EmployeeId, x.StartDate, ct); if (m.Value?.ManagerId == s.Value!.EmployeeId) allowed.Add(Map(x)); } return Result<PagedResult<OnDutyDto>>.Success(Page(allowed, query)); }
    public Task<Result<OnDutyDto>> ApproveOnDutyAsync(Guid id, CancellationToken ct = default) => TransitionOnDuty(id, AttendanceRequestStatus.Approved, null, ct);
    public Task<Result<OnDutyDto>> RejectOnDutyAsync(Guid id, string comments, CancellationToken ct = default) => TransitionOnDuty(id, AttendanceRequestStatus.Rejected, comments, ct);

    private async Task<Result<RegularizationDto>> TransitionRegularization(Guid id, AttendanceRequestStatus target, string? comments, CancellationToken ct)
    {
        var s = await Subject(ct); if (!s.Succeeded) return Fail<RegularizationDto>(s);
        var x = await db.AttendanceRegularizationRequests.Include(x => x.Events).SingleOrDefaultAsync(x => x.Id == id, ct); if (x is null) return Result<RegularizationDto>.NotFound("Regularization request was not found.");
        if (target == AttendanceRequestStatus.Cancelled) { if (x.EmployeeId != s.Value!.EmployeeId) return Result<RegularizationDto>.NotFound("Regularization request was not found."); } else { if (x.EmployeeId == s.Value!.EmployeeId) return Result<RegularizationDto>.Forbidden("An employee cannot approve or reject their own request."); var m = await managers.ResolveAsync(x.EmployeeId, x.BusinessDate, ct); if (m.Value?.ManagerId != s.Value!.EmployeeId) return Result<RegularizationDto>.Forbidden("The employee is not an effective report for this date."); }
        if (x.Status != AttendanceRequestStatus.Pending) return Result<RegularizationDto>.Conflict("The request has already been processed.");
        if (periodLock is not null && !(await periodLock.EnsureDateIsOpenAsync(x.BusinessDate, ct)).Succeeded) return Result<RegularizationDto>.Conflict("The Attendance period is closed and must be reopened before this change.");
        await using var transaction = await db.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var reviewerId = target == AttendanceRequestStatus.Cancelled ? (Guid?)null : s.Value!.UserId;
        var changed = await db.AttendanceRegularizationRequests
            .Where(r => r.Id == x.Id && r.Status == AttendanceRequestStatus.Pending && r.ConcurrencyVersion == x.ConcurrencyVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, target)
                .SetProperty(r => r.ConcurrencyVersion, r => r.ConcurrencyVersion + 1)
                .SetProperty(r => r.ReviewedByUserId, reviewerId)
                .SetProperty(r => r.ReviewedAtUtc, target == AttendanceRequestStatus.Cancelled ? null : now)
                .SetProperty(r => r.ReviewerComments, comments == null ? null : comments.Trim()), ct);
        if (changed != 1) return Result<RegularizationDto>.Conflict("The regularization request was processed by another action.");
        x.Status = target; x.ConcurrencyVersion++; x.ReviewedByUserId = reviewerId; x.ReviewedAtUtc = target == AttendanceRequestStatus.Cancelled ? null : now; x.ReviewerComments = comments?.Trim();
        var workflowEvent = Event(x.TenantId, x.Id, target switch { AttendanceRequestStatus.Approved => AttendanceRequestEventType.Approved, AttendanceRequestStatus.Rejected => AttendanceRequestEventType.Rejected, _ => AttendanceRequestEventType.Cancelled }, s.Value!.UserId, now, comments);
        db.ClearChangeTracker();
        db.AttendanceRegularizationEvents.Add(workflowEvent);
        if (target == AttendanceRequestStatus.Approved) db.AttendanceAdjustments.Add(new AttendanceAdjustment { Id = Guid.NewGuid(), TenantId = x.TenantId, EmployeeId = x.EmployeeId, BusinessDate = x.BusinessDate, AttendanceRegularizationRequestId = x.Id, EffectiveInAtUtc = x.ProposedInAtUtc, EffectiveOutAtUtc = x.ProposedOutAtUtc, ApprovedByUserId = s.Value.UserId, ApprovedAtUtc = now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<RegularizationDto>.Conflict("The regularization request was processed by another action.");
        }
        if (target == AttendanceRequestStatus.Approved)
        {
            var processed = await processor.ProcessAsync(x.EmployeeId, x.BusinessDate, ct);
            if (!processed.Succeeded) return Result<RegularizationDto>.Failure(processed.Status, processed.Message, processed.Errors);
        }
        await transaction.CommitAsync(ct);
        return Result<RegularizationDto>.Success(Map(x));
    }

    private async Task<Result<OnDutyDto>> TransitionOnDuty(Guid id, AttendanceRequestStatus target, string? comments, CancellationToken ct)
    {
        var s = await Subject(ct); if (!s.Succeeded) return Fail<OnDutyDto>(s);
        var x = await db.AttendanceOnDutyRequests.Include(x => x.Events).SingleOrDefaultAsync(x => x.Id == id, ct); if (x is null) return Result<OnDutyDto>.NotFound("On Duty request was not found."); if (target != AttendanceRequestStatus.Cancelled && x.EmployeeId == s.Value!.EmployeeId) return Result<OnDutyDto>.Forbidden("An employee cannot approve or reject their own request."); if (target == AttendanceRequestStatus.Cancelled ? x.EmployeeId != s.Value!.EmployeeId : (await managers.ResolveAsync(x.EmployeeId, x.StartDate, ct)).Value?.ManagerId != s.Value!.EmployeeId) return Result<OnDutyDto>.Forbidden("You are not authorized for this On Duty request."); if (x.Status != AttendanceRequestStatus.Pending) return Result<OnDutyDto>.Conflict("The request has already been processed.");
        if (target == AttendanceRequestStatus.Approved && await db.LeaveRequestDays.AnyAsync(d => d.TenantId == x.TenantId && d.Date >= x.StartDate && d.Date <= x.EndDate && d.LeaveRequest != null && d.LeaveRequest.EmployeeId == x.EmployeeId && d.LeaveRequest.Status == LeaveRequestStatus.Approved, ct)) return Result<OnDutyDto>.Conflict("Approved Leave overlaps this On Duty request.");
        if (periodLock is not null && !(await periodLock.EnsureRangeIsOpenAsync(x.StartDate, x.EndDate, ct)).Succeeded) return Result<OnDutyDto>.Conflict("The Attendance period is closed and must be reopened before this change.");
        await using var transaction = await db.BeginTransactionAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var reviewerId = target == AttendanceRequestStatus.Cancelled ? (Guid?)null : s.Value!.UserId;
        var changed = await db.AttendanceOnDutyRequests
            .Where(r => r.Id == x.Id && r.Status == AttendanceRequestStatus.Pending && r.ConcurrencyVersion == x.ConcurrencyVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, target)
                .SetProperty(r => r.ConcurrencyVersion, r => r.ConcurrencyVersion + 1)
                .SetProperty(r => r.ReviewedByUserId, reviewerId)
                .SetProperty(r => r.ReviewedAtUtc, target == AttendanceRequestStatus.Cancelled ? null : now)
                .SetProperty(r => r.ReviewerComments, comments == null ? null : comments.Trim()), ct);
        if (changed != 1) return Result<OnDutyDto>.Conflict("The On Duty request was processed by another action.");
        x.Status = target; x.ConcurrencyVersion++; x.ReviewedByUserId = reviewerId; x.ReviewedAtUtc = target == AttendanceRequestStatus.Cancelled ? null : now; x.ReviewerComments = comments?.Trim();
        var workflowEvent = OnDutyEvent(x.TenantId, x.Id, target switch { AttendanceRequestStatus.Approved => AttendanceRequestEventType.Approved, AttendanceRequestStatus.Rejected => AttendanceRequestEventType.Rejected, _ => AttendanceRequestEventType.Cancelled }, s.Value!.UserId, now, comments);
        db.ClearChangeTracker();
        db.AttendanceOnDutyEvents.Add(workflowEvent);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<OnDutyDto>.Conflict("The On Duty request was processed by another action.");
        }
        if (target == AttendanceRequestStatus.Approved)
        {
            for (var date = x.StartDate; date <= x.EndDate; date = date.AddDays(1))
            {
                var processed = await processor.ProcessAsync(x.EmployeeId, date, ct);
                if (!processed.Succeeded) return Result<OnDutyDto>.Failure(processed.Status, processed.Message, processed.Errors);
            }
        }
        await transaction.CommitAsync(ct);
        return Result<OnDutyDto>.Success(Map(x));
    }

    private async Task<Result<RuntimeEmployeeIdentity>> Subject(CancellationToken ct) => await identity.ResolveCurrentAsync(ct);
    private static Result<T> Fail<T>(Result<RuntimeEmployeeIdentity> result) => Result<T>.Failure(result.Status, result.Message, result.Errors);
    private static AttendanceRegularizationEvent Event(Guid tenantId, Guid requestId, AttendanceRequestEventType type, Guid actor, DateTime at, string? comments) => new() { Id = Guid.NewGuid(), TenantId = tenantId, AttendanceRegularizationRequestId = requestId, EventType = type, ActorUserId = actor, OccurredAtUtc = at, Comments = comments };
    private static AttendanceOnDutyEvent OnDutyEvent(Guid tenantId, Guid requestId, AttendanceRequestEventType type, Guid actor, DateTime at, string? comments) => new() { Id = Guid.NewGuid(), TenantId = tenantId, AttendanceOnDutyRequestId = requestId, EventType = type, ActorUserId = actor, OccurredAtUtc = at, Comments = comments };
    private static RegularizationDto Map(AttendanceRegularizationRequest x) => new(x.Id, x.EmployeeId, x.BusinessDate, x.RequestType, x.ProposedInAtUtc, x.ProposedOutAtUtc, x.Reason, x.Status, x.SubmittedAtUtc, x.ReviewedAtUtc, x.ReviewerComments, x.Events.OrderBy(e => e.OccurredAtUtc).Select(e => new AttendanceWorkflowEventDto(e.EventType, e.ActorUserId, e.OccurredAtUtc, e.Comments)).ToList());
    private static OnDutyDto Map(AttendanceOnDutyRequest x) => new(x.Id, x.EmployeeId, x.StartDate, x.EndDate, x.Reason, x.Purpose, x.Location, x.Status, x.SubmittedAtUtc, x.ReviewedAtUtc, x.ReviewerComments, x.Events.OrderBy(e => e.OccurredAtUtc).Select(e => new AttendanceWorkflowEventDto(e.EventType, e.ActorUserId, e.OccurredAtUtc, e.Comments)).ToList());
    private static async Task<PagedResult<RegularizationDto>> Page(IQueryable<AttendanceRegularizationRequest> q, PagedQuery p, CancellationToken ct) { var total = await q.CountAsync(ct); return new(await q.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize).Select(x => new RegularizationDto(x.Id, x.EmployeeId, x.BusinessDate, x.RequestType, x.ProposedInAtUtc, x.ProposedOutAtUtc, x.Reason, x.Status, x.SubmittedAtUtc, x.ReviewedAtUtc, x.ReviewerComments, new List<AttendanceWorkflowEventDto>())).ToListAsync(ct), p.Page, p.PageSize, total); }
    private static PagedResult<RegularizationDto> Page(List<RegularizationDto> x, PagedQuery p) => new(x.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize).ToList(), p.Page, p.PageSize, x.Count);
    private static async Task<PagedResult<OnDutyDto>> Page(IQueryable<AttendanceOnDutyRequest> q, PagedQuery p, CancellationToken ct) { var total = await q.CountAsync(ct); return new(await q.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize).Select(x => new OnDutyDto(x.Id, x.EmployeeId, x.StartDate, x.EndDate, x.Reason, x.Purpose, x.Location, x.Status, x.SubmittedAtUtc, x.ReviewedAtUtc, x.ReviewerComments, new List<AttendanceWorkflowEventDto>())).ToListAsync(ct), p.Page, p.PageSize, total); }
    private static PagedResult<OnDutyDto> Page(List<OnDutyDto> x, PagedQuery p) => new(x.Skip((p.Page - 1) * p.PageSize).Take(p.PageSize).ToList(), p.Page, p.PageSize, x.Count);
}
