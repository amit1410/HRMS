using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using SeparationReasonEntity = HRMS.Domain.Entities.Separation.SeparationReason;

namespace HRMS.Application.Services;

public sealed class SeparationService(IHrmsDbContext db, ITenantContext tenant, IEmployeeIdentityResolver identity, IEmployeeManagerResolver managerResolver, TimeProvider clock) : ISeparationService
{
    private static readonly HashSet<EmployeeSeparationStatus> Terminal = [EmployeeSeparationStatus.Rejected, EmployeeSeparationStatus.Withdrawn, EmployeeSeparationStatus.Cancelled, EmployeeSeparationStatus.Exited];

    public async Task<Result<IReadOnlyList<SeparationReasonDto>>> GetReasonsAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<IReadOnlyList<SeparationReasonDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.SeparationReasons.AsNoTracking().Where(x => x.TenantId == id).OrderBy(x => x.DisplayOrder).ThenBy(x => x.Code).ToListAsync(ct);
        return Result<IReadOnlyList<SeparationReasonDto>>.Success(rows.Select(ToReason).ToList());
    }

    public async Task<Result<SeparationReasonDto>> CreateReasonAsync(SeparationReasonRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid id) return Result<SeparationReasonDto>.Unauthorized("No authenticated tenant.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) return Result<SeparationReasonDto>.Invalid("code", "Reason code and name are required.");
        var code = request.Code.Trim().ToUpperInvariant(); if (await db.SeparationReasons.AnyAsync(x => x.TenantId == id && x.Code == code, ct)) return Result<SeparationReasonDto>.Conflict("A separation reason with this code already exists.");
        var row = new SeparationReasonEntity { Id = Guid.NewGuid(), TenantId = id, Code = code, Name = request.Name.Trim(), Description = request.Description?.Trim(), Category = request.Category, EmployeeInitiatedAllowed = request.EmployeeInitiatedAllowed, EmployerInitiatedAllowed = request.EmployerInitiatedAllowed, IsActive = request.IsActive, EffectiveFrom = request.EffectiveFrom ?? DateOnly.FromDateTime(clock.GetUtcNow().DateTime), EffectiveTo = request.EffectiveTo, DisplayOrder = request.DisplayOrder };
        if (row.EffectiveTo < row.EffectiveFrom) return Result<SeparationReasonDto>.Invalid("effectiveTo", "Effective To cannot precede Effective From.");
        db.SeparationReasons.Add(row); await db.SaveChangesAsync(ct); return Result<SeparationReasonDto>.Success(ToReason(row));
    }

    public async Task<Result<SeparationReasonDto>> UpdateReasonAsync(Guid id, SeparationReasonRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SeparationReasonDto>.Unauthorized("No authenticated tenant.");
        var row = await db.SeparationReasons.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); if (row is null) return Result<SeparationReasonDto>.NotFound("Separation reason not found.");
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) return Result<SeparationReasonDto>.Invalid("code", "Reason code and name are required.");
        var code = request.Code.Trim().ToUpperInvariant(); if (await db.SeparationReasons.AnyAsync(x => x.TenantId == tenantId && x.Id != id && x.Code == code, ct)) return Result<SeparationReasonDto>.Conflict("A separation reason with this code already exists.");
        if ((request.EffectiveTo ?? DateOnly.MaxValue) < (request.EffectiveFrom ?? row.EffectiveFrom)) return Result<SeparationReasonDto>.Invalid("effectiveTo", "Effective To cannot precede Effective From.");
        row.Code = code; row.Name = request.Name.Trim(); row.Description = request.Description?.Trim(); row.Category = request.Category; row.EmployeeInitiatedAllowed = request.EmployeeInitiatedAllowed; row.EmployerInitiatedAllowed = request.EmployerInitiatedAllowed; row.IsActive = request.IsActive; row.EffectiveFrom = request.EffectiveFrom ?? row.EffectiveFrom; row.EffectiveTo = request.EffectiveTo; row.DisplayOrder = request.DisplayOrder; await db.SaveChangesAsync(ct); return Result<SeparationReasonDto>.Success(ToReason(row));
    }

    public async Task<Result<EmployeeSeparationDto>> CreateSelfAsync(SeparationRequest request, CancellationToken ct = default)
    {
        var current = await identity.ResolveCurrentAsync(ct); if (!current.Succeeded) return Result<EmployeeSeparationDto>.Failure(current.Status, current.Message, current.Errors);
        return await CreateCoreAsync(current.Value!.EmployeeId, request.ReasonId, request.RequestDate, request.ProposedLastWorkingDate, request.Remarks, SeparationType.EmployeeInitiated, EmployeeSeparationInitiator.Employee, current.Value.UserId, ct);
    }

    public Task<Result<EmployeeSeparationDto>> CreateForEmployeeAsync(Guid employeeId, HrSeparationRequest request, CancellationToken ct = default) => CreateCoreAsync(employeeId, request.ReasonId, request.RequestDate, request.ProposedLastWorkingDate, request.Remarks, SeparationType.EmployerInitiated, EmployeeSeparationInitiator.Hr, tenant.UserId, ct);

    private async Task<Result<EmployeeSeparationDto>> CreateCoreAsync(Guid employeeId, Guid reasonId, DateOnly requestDate, DateOnly lwd, string? remarks, SeparationType type, EmployeeSeparationInitiator initiator, Guid? actor, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSeparationDto>.Unauthorized("No authenticated tenant.");
        if (employeeId == Guid.Empty || !await db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == employeeId, ct)) return Result<EmployeeSeparationDto>.NotFound("Employee not found.");
        if (lwd < requestDate) return Result<EmployeeSeparationDto>.Invalid("proposedLastWorkingDate", "Last working date cannot precede the request date.");
        var reason = await db.SeparationReasons.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == reasonId, ct); if (reason is null) return Result<EmployeeSeparationDto>.Invalid("reasonId", "Separation reason was not found in the tenant.");
        var today = DateOnly.FromDateTime(clock.GetUtcNow().DateTime); if (!reason.IsActive || reason.EffectiveFrom > today || reason.EffectiveTo < today) return Result<EmployeeSeparationDto>.Invalid("reasonId", "Separation reason is inactive or outside its effective dates.");
        if (type == SeparationType.EmployeeInitiated && !reason.EmployeeInitiatedAllowed || type == SeparationType.EmployerInitiated && !reason.EmployerInitiatedAllowed) return Result<EmployeeSeparationDto>.Invalid("reasonId", "This separation reason is not allowed for the selected initiator.");
        if (await db.EmployeeSeparations.AnyAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && !Terminal.Contains(x.Status), ct)) return Result<EmployeeSeparationDto>.Conflict("The employee already has an active separation case.");
        var employment = await db.EmployeeEmployments.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId, ct);
        var caseRow = new EmployeeSeparation { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, ActiveEmployeeKey = employeeId, SeparationNumber = $"SEP-{today:yyyy}-{Guid.NewGuid():N}"[..16].ToUpperInvariant(), SeparationType = type, ReasonId = reasonId, InitiatedBy = initiator, InitiatedByUserId = actor, RequestDate = requestDate, ProposedLastWorkingDate = lwd, NoticeStartDate = null, NoticeEndDate = null, NoticePeriodDays = employment?.NoticePeriod, EmployeeRemarks = type == SeparationType.EmployeeInitiated ? remarks?.Trim() : null, HrRemarks = type == SeparationType.EmployerInitiated ? remarks?.Trim() : null, CreatedByUserId = actor, ModifiedByUserId = actor };
        db.EmployeeSeparations.Add(caseRow); AddEvent(caseRow, EmployeeSeparationEventType.Created, null, caseRow.Status, actor, null, remarks);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<EmployeeSeparationDto>.Conflict("The employee already has an active separation case or the request changed concurrently.");
        }
        return Result<EmployeeSeparationDto>.Success(ToDto(caseRow, reason));
    }

    public async Task<Result<EmployeeSeparationDto>> GetCurrentSelfAsync(CancellationToken ct = default) { var current = await identity.ResolveCurrentAsync(ct); if (!current.Succeeded) return Result<EmployeeSeparationDto>.Failure(current.Status, current.Message, current.Errors); var row = await db.EmployeeSeparations.Include(x => x.Reason).Where(x => x.TenantId == current.Value!.TenantId && x.EmployeeId == current.Value.EmployeeId && !Terminal.Contains(x.Status)).OrderByDescending(x => x.CreatedDate).FirstOrDefaultAsync(ct); return row is null ? Result<EmployeeSeparationDto>.NotFound("No active separation case exists.") : Result<EmployeeSeparationDto>.Success(ToDto(row, row.Reason!)); }
    public async Task<Result<IReadOnlyList<EmployeeSeparationDto>>> GetMineAsync(CancellationToken ct = default) { var current = await identity.ResolveCurrentAsync(ct); if (!current.Succeeded) return Result<IReadOnlyList<EmployeeSeparationDto>>.Failure(current.Status, current.Message, current.Errors); var rows = await db.EmployeeSeparations.Include(x => x.Reason).Where(x => x.TenantId == current.Value!.TenantId && x.EmployeeId == current.Value.EmployeeId).OrderByDescending(x => x.CreatedDate).ToListAsync(ct); return Result<IReadOnlyList<EmployeeSeparationDto>>.Success(rows.Select(x => ToDto(x, x.Reason!)).ToList()); }
    public async Task<Result<EmployeeSeparationDto>> GetAsync(Guid id, CancellationToken ct = default) { if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSeparationDto>.Unauthorized("No authenticated tenant."); var row = await db.EmployeeSeparations.Include(x => x.Reason).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); return row is null ? Result<EmployeeSeparationDto>.NotFound("Separation case not found.") : Result<EmployeeSeparationDto>.Success(ToDto(row, row.Reason!)); }
    public async Task<Result<IReadOnlyList<EmployeeSeparationDto>>> GetAllAsync(CancellationToken ct = default) { if (tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<EmployeeSeparationDto>>.Unauthorized("No authenticated tenant."); var rows = await db.EmployeeSeparations.Include(x => x.Reason).Where(x => x.TenantId == tenantId).OrderByDescending(x => x.CreatedDate).Take(500).ToListAsync(ct); return Result<IReadOnlyList<EmployeeSeparationDto>>.Success(rows.Select(x => ToDto(x, x.Reason!)).ToList()); }
    public async Task<Result<IReadOnlyList<EmployeeSeparationDto>>> GetTeamAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenant.UserId is not Guid)
            return Result<IReadOnlyList<EmployeeSeparationDto>>.Unauthorized("No authenticated tenant and user.");
        var managerIdentity = await identity.ResolveCurrentAsync(ct);
        if (!managerIdentity.Succeeded)
            return Result<IReadOnlyList<EmployeeSeparationDto>>.Failure(managerIdentity.Status, managerIdentity.Message, managerIdentity.Errors);
        var candidateIds = await db.EmployeeSeparations.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Select(x => x.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
        var asOf = DateOnly.FromDateTime(clock.GetUtcNow().DateTime);
        var teamIds = new List<Guid>();
        foreach (var employeeId in candidateIds)
        {
            var manager = await managerResolver.ResolveAsync(employeeId, asOf, ct);
            if (manager.Succeeded && manager.Value?.ManagerId == managerIdentity.Value!.EmployeeId)
                teamIds.Add(employeeId);
        }
        var rows = await db.EmployeeSeparations.Include(x => x.Reason)
            .Where(x => x.TenantId == tenantId && teamIds.Contains(x.EmployeeId))
            .OrderByDescending(x => x.CreatedDate)
            .ToListAsync(ct);
        return Result<IReadOnlyList<EmployeeSeparationDto>>.Success(rows.Select(x => ToDto(x, x.Reason!)).ToList());
    }
    public async Task<Result<IReadOnlyList<SeparationEventDto>>> GetHistoryAsync(Guid id, CancellationToken ct = default) { if (tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<SeparationEventDto>>.Unauthorized("No authenticated tenant."); if (!await db.EmployeeSeparations.AnyAsync(x => x.TenantId == tenantId && x.Id == id, ct)) return Result<IReadOnlyList<SeparationEventDto>>.NotFound("Separation case not found."); var rows = await db.EmployeeSeparationEvents.AsNoTracking().Where(x => x.TenantId == tenantId && x.EmployeeSeparationId == id).OrderBy(x => x.OccurredAtUtc).ToListAsync(ct); return Result<IReadOnlyList<SeparationEventDto>>.Success(rows.Select(x => new SeparationEventDto(x.Id, x.EventType, x.FromStatus, x.ToStatus, x.ActorUserId, x.OccurredAtUtc, x.Reason, x.Comment)).ToList()); }

    public Task<Result<EmployeeSeparationDto>> SubmitAsync(Guid id, CancellationToken ct = default) => SubmitCoreAsync(id, ct);
    public Task<Result<EmployeeSeparationDto>> WithdrawAsync(Guid id, CancellationToken ct = default) => TransitionAsync(id, EmployeeSeparationStatus.Withdrawn, EmployeeSeparationEventType.Withdrawn, [EmployeeSeparationStatus.Draft, EmployeeSeparationStatus.Submitted, EmployeeSeparationStatus.ManagerReview], ct);

    private async Task<Result<EmployeeSeparationDto>> SubmitCoreAsync(Guid id, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSeparationDto>.Unauthorized("No authenticated tenant.");
        var row = await db.EmployeeSeparations.Include(x => x.Reason).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (row is null) return Result<EmployeeSeparationDto>.NotFound("Separation case not found.");
        if (row.Status != EmployeeSeparationStatus.Draft) return Result<EmployeeSeparationDto>.Conflict($"Separation cannot transition from {row.Status} to Submitted.");
        var previous = row.Status;
        row.Status = row.SeparationType == SeparationType.EmployeeInitiated ? EmployeeSeparationStatus.ManagerReview : EmployeeSeparationStatus.HrReview;
        row.ModifiedByUserId = tenant.UserId;
        row.ModifiedDate = clock.GetUtcNow().UtcDateTime;
        row.ConcurrencyVersion++;
        AddEvent(row, EmployeeSeparationEventType.Submitted, previous, row.Status, tenant.UserId, null, null);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Result<EmployeeSeparationDto>.Conflict("The separation was changed by another operation."); }
        return Result<EmployeeSeparationDto>.Success(ToDto(row, row.Reason!));
    }

    public async Task<Result<EmployeeSeparationDto>> ManagerApproveAsync(Guid id, CancellationToken ct = default)
    {
        var loaded = await LoadForActionAsync(id, EmployeeSeparationStatus.ManagerReview, ct);
        if (!loaded.Succeeded) return Result<EmployeeSeparationDto>.Failure(loaded.Status, loaded.Message, loaded.Errors);
        var row = loaded.Value!;
        var actor = await identity.ResolveCurrentAsync(ct);
        if (!actor.Succeeded) return Result<EmployeeSeparationDto>.Failure(actor.Status, actor.Message, actor.Errors);
        if (actor.Value!.EmployeeId == row.EmployeeId) return Result<EmployeeSeparationDto>.Forbidden("An employee cannot review their own separation.");
        var manager = await managerResolver.ResolveAsync(row.EmployeeId, row.RequestDate, ct);
        if (!manager.Succeeded || manager.Value?.ManagerId != actor.Value.EmployeeId)
            return Result<EmployeeSeparationDto>.Forbidden("You are not the effective manager for this separation.");
        return await ReviewTransitionAsync(row, EmployeeSeparationStatus.HrReview, EmployeeSeparationEventType.ManagerApproved, actor.Value.UserId, null, null, ct);
    }

    public async Task<Result<EmployeeSeparationDto>> ManagerRejectAsync(Guid id, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result<EmployeeSeparationDto>.Invalid("reason", "A rejection reason is required.");
        var loaded = await LoadForActionAsync(id, EmployeeSeparationStatus.ManagerReview, ct);
        if (!loaded.Succeeded) return Result<EmployeeSeparationDto>.Failure(loaded.Status, loaded.Message, loaded.Errors);
        var row = loaded.Value!;
        var actor = await identity.ResolveCurrentAsync(ct);
        if (!actor.Succeeded) return Result<EmployeeSeparationDto>.Failure(actor.Status, actor.Message, actor.Errors);
        var manager = await managerResolver.ResolveAsync(row.EmployeeId, row.RequestDate, ct);
        if (actor.Value!.EmployeeId == row.EmployeeId || !manager.Succeeded || manager.Value?.ManagerId != actor.Value.EmployeeId)
            return Result<EmployeeSeparationDto>.Forbidden("You are not authorized to reject this separation.");
        row.ManagerRemarks = reason.Trim();
        return await ReviewTransitionAsync(row, EmployeeSeparationStatus.Rejected, EmployeeSeparationEventType.ManagerRejected, actor.Value.UserId, reason.Trim(), reason.Trim(), ct);
    }

    public async Task<Result<EmployeeSeparationDto>> HrApproveAsync(Guid id, CancellationToken ct = default)
    {
        var loaded = await LoadForActionAsync(id, EmployeeSeparationStatus.HrReview, ct);
        if (!loaded.Succeeded) return Result<EmployeeSeparationDto>.Failure(loaded.Status, loaded.Message, loaded.Errors);
        var row = loaded.Value!;
        if (row.InitiatedByUserId is Guid maker && maker == tenant.UserId)
            return Result<EmployeeSeparationDto>.Forbidden("The maker cannot perform final approval.");
        if (tenant.TenantId is not Guid tenantId || tenant.UserId is not Guid actor)
            return Result<EmployeeSeparationDto>.Unauthorized("No authenticated tenant and user.");
        var employment = await db.EmployeeEmployments.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == row.EmployeeId, ct);
        if (employment is null) return Result<EmployeeSeparationDto>.Conflict("An employment record is required before approval.");
        var approvedLwd = row.ProposedLastWorkingDate;
        if (approvedLwd < row.RequestDate || approvedLwd < employment.DateOfJoining)
            return Result<EmployeeSeparationDto>.Invalid("approvedLastWorkingDate", "Approved last working date is outside the employment interval.");
        if (await db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == row.EmployeeId && x.DateOfLeaving != null && x.DateOfLeaving < approvedLwd, ct))
            return Result<EmployeeSeparationDto>.Invalid("approvedLastWorkingDate", "Approved last working date is after the employment end date.");
        var noticeStart = row.NoticeStartDate ?? row.RequestDate;
        if (approvedLwd < noticeStart) return Result<EmployeeSeparationDto>.Invalid("approvedLastWorkingDate", "Approved last working date cannot precede the notice start date.");
        var served = Math.Max(0, approvedLwd.DayNumber - noticeStart.DayNumber + 1);
        row.ApprovedLastWorkingDate = approvedLwd;
        row.NoticeStartDate = noticeStart;
        row.NoticeEndDate = approvedLwd;
        row.NoticeServedDays = served;
        row.NoticeShortfallDays = row.NoticePeriodDays is int required ? Math.Max(0, required - served) : null;
        row.Status = EmployeeSeparationStatus.Approved;
        row.ModifiedByUserId = actor;
        row.ModifiedDate = clock.GetUtcNow().UtcDateTime;
        row.ConcurrencyVersion++;
        employment.NoticeStatus = NoticePeriodStatus.Active;
        employment.NoticeStartDate = noticeStart;
        employment.NoticeEndDate = approvedLwd;
        AddEvent(row, EmployeeSeparationEventType.HrApproved, EmployeeSeparationStatus.HrReview, row.Status, actor, null, null);
        AddEvent(row, EmployeeSeparationEventType.NoticePeriodActivated, row.Status, row.Status, actor, null, $"Notice period active through {approvedLwd:yyyy-MM-dd}.");
        try
        {
            await using var transaction = await db.BeginTransactionAsync(ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { return Result<EmployeeSeparationDto>.Conflict("The separation or employment was changed concurrently."); }
        catch (DbUpdateException) { return Result<EmployeeSeparationDto>.Conflict("Approval could not be synchronized with employment notice fields."); }
        return Result<EmployeeSeparationDto>.Success(ToDto(row, row.Reason!));
    }

    public async Task<Result<EmployeeSeparationDto>> HrRejectAsync(Guid id, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) return Result<EmployeeSeparationDto>.Invalid("reason", "A rejection reason is required.");
        var loaded = await LoadForActionAsync(id, EmployeeSeparationStatus.HrReview, ct);
        if (!loaded.Succeeded) return Result<EmployeeSeparationDto>.Failure(loaded.Status, loaded.Message, loaded.Errors);
        var row = loaded.Value!;
        if (row.InitiatedByUserId is Guid maker && maker == tenant.UserId) return Result<EmployeeSeparationDto>.Forbidden("The maker cannot perform final rejection.");
        row.HrRemarks = reason.Trim();
        return await ReviewTransitionAsync(row, EmployeeSeparationStatus.Rejected, EmployeeSeparationEventType.HrRejected, tenant.UserId, reason.Trim(), reason.Trim(), ct);
    }

    public async Task<Result<EmployeeSeparationDto>> ReviseLwdAsync(Guid id, SeparationLwdRevisionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason)) return Result<EmployeeSeparationDto>.Invalid("reason", "A reason is required for an LWD revision.");
        var loaded = await LoadForActionAsync(id, EmployeeSeparationStatus.HrReview, ct);
        if (!loaded.Succeeded) return Result<EmployeeSeparationDto>.Failure(loaded.Status, loaded.Message, loaded.Errors);
        var row = loaded.Value!;
        if (request.NewLastWorkingDate < row.RequestDate) return Result<EmployeeSeparationDto>.Invalid("newLastWorkingDate", "Last working date cannot precede the request date.");
        var old = row.ProposedLastWorkingDate;
        row.ProposedLastWorkingDate = request.NewLastWorkingDate;
        row.HrRemarks = request.Reason.Trim();
        row.ModifiedByUserId = tenant.UserId;
        row.ModifiedDate = clock.GetUtcNow().UtcDateTime;
        row.ConcurrencyVersion++;
        AddEvent(row, EmployeeSeparationEventType.LwdRevised, row.Status, row.Status, tenant.UserId, request.Reason.Trim(), $"{old:yyyy-MM-dd} -> {request.NewLastWorkingDate:yyyy-MM-dd}");
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<EmployeeSeparationDto>.Conflict("The separation was changed concurrently."); }
        return Result<EmployeeSeparationDto>.Success(ToDto(row, row.Reason!));
    }

    public async Task<Result<IReadOnlyList<EmployeeSeparationDto>>> GetManagerInboxAsync(CancellationToken ct = default)
    {
        var result = await GetTeamAsync(ct);
        return result.Succeeded ? Result<IReadOnlyList<EmployeeSeparationDto>>.Success(result.Value!.Where(x => x.Status == EmployeeSeparationStatus.ManagerReview).ToList()) : result;
    }

    public async Task<Result<IReadOnlyList<EmployeeSeparationDto>>> GetHrInboxAsync(CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<EmployeeSeparationDto>>.Unauthorized("No authenticated tenant.");
        var rows = await db.EmployeeSeparations.Include(x => x.Reason).Where(x => x.TenantId == tenantId && x.Status == EmployeeSeparationStatus.HrReview).OrderBy(x => x.CreatedDate).Take(500).ToListAsync(ct);
        return Result<IReadOnlyList<EmployeeSeparationDto>>.Success(rows.Select(x => ToDto(x, x.Reason!)).ToList());
    }

    private async Task<Result<EmployeeSeparation>> LoadForActionAsync(Guid id, EmployeeSeparationStatus expected, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSeparation>.Unauthorized("No authenticated tenant.");
        var row = await db.EmployeeSeparations.Include(x => x.Reason).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct);
        if (row is null) return Result<EmployeeSeparation>.NotFound("Separation case not found.");
        return row.Status != expected ? Result<EmployeeSeparation>.Conflict($"Separation must be in {expected} status.") : Result<EmployeeSeparation>.Success(row);
    }

    private async Task<Result<EmployeeSeparationDto>> ReviewTransitionAsync(EmployeeSeparation row, EmployeeSeparationStatus target, EmployeeSeparationEventType eventType, Guid? actor, string? reason, string? comment, CancellationToken ct)
    {
        var previous = row.Status;
        row.Status = target;
        row.ModifiedByUserId = actor;
        row.ModifiedDate = clock.GetUtcNow().UtcDateTime;
        row.ConcurrencyVersion++;
        if (Terminal.Contains(target)) row.ActiveEmployeeKey = null;
        AddEvent(row, eventType, previous, target, actor, reason, comment);
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<EmployeeSeparationDto>.Conflict("The separation was changed concurrently."); }
        return Result<EmployeeSeparationDto>.Success(ToDto(row, row.Reason!));
    }
    private async Task<Result<EmployeeSeparationDto>> TransitionAsync(Guid id, EmployeeSeparationStatus target, EmployeeSeparationEventType eventType, EmployeeSeparationStatus[] allowed, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<EmployeeSeparationDto>.Unauthorized("No authenticated tenant."); var row = await db.EmployeeSeparations.Include(x => x.Reason).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, ct); if (row is null) return Result<EmployeeSeparationDto>.NotFound("Separation case not found.");
        if (!allowed.Contains(row.Status)) return Result<EmployeeSeparationDto>.Conflict($"Separation cannot transition from {row.Status} to {target}.");
        if (target == EmployeeSeparationStatus.Withdrawn && tenant.UserId is Guid userId && row.InitiatedByUserId != userId) return Result<EmployeeSeparationDto>.Forbidden("Only the initiating employee can withdraw this request.");
        var previous = row.Status; row.Status = target; row.ModifiedByUserId = tenant.UserId; row.ModifiedDate = clock.GetUtcNow().UtcDateTime; row.ConcurrencyVersion++; if (Terminal.Contains(target)) row.ActiveEmployeeKey = null; AddEvent(row, eventType, previous, target, tenant.UserId, null, null); try { await db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { return Result<EmployeeSeparationDto>.Conflict("The separation was changed by another operation."); } return Result<EmployeeSeparationDto>.Success(ToDto(row, row.Reason!));
    }
    private void AddEvent(EmployeeSeparation row, EmployeeSeparationEventType type, EmployeeSeparationStatus? from, EmployeeSeparationStatus? to, Guid? actor, string? reason, string? comment) => db.EmployeeSeparationEvents.Add(new EmployeeSeparationEvent { Id = Guid.NewGuid(), TenantId = row.TenantId, EmployeeSeparationId = row.Id, EventType = type, FromStatus = from, ToStatus = to, ActorUserId = actor, OccurredAtUtc = clock.GetUtcNow().UtcDateTime, Reason = reason, Comment = comment });
    private static SeparationReasonDto ToReason(SeparationReasonEntity x) => new(x.Id, x.Code, x.Name, x.Description, x.Category, x.EmployeeInitiatedAllowed, x.EmployerInitiatedAllowed, x.IsActive, x.EffectiveFrom, x.EffectiveTo, x.DisplayOrder);
    private static EmployeeSeparationDto ToDto(EmployeeSeparation x, SeparationReasonEntity reason) => new(x.Id, x.EmployeeId, x.SeparationNumber, x.SeparationType, x.ReasonId, reason.Code, reason.Name, x.InitiatedBy, x.RequestDate, x.ProposedLastWorkingDate, x.ApprovedLastWorkingDate, x.NoticeStartDate, x.NoticeEndDate, x.NoticePeriodDays, x.NoticeServedDays, x.NoticeShortfallDays, x.EmployeeRemarks, x.ManagerRemarks, x.HrRemarks, x.Status, x.ConcurrencyVersion, x.CreatedDate);
}
