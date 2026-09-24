using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using PayrollSeparationReason = HRMS.Domain.Enums.SeparationReason;

namespace HRMS.Application.Services;

public sealed class SeparationSettlementOrchestrationService(
    IHrmsDbContext db,
    ITenantContext tenant,
    IPayrollRetroSettlementService payrollSettlement,
    TimeProvider clock,
    ISeparationSettlementFailureInjector? failureInjector = null) : ISeparationSettlementOrchestrationService
{
    public async Task<Result<SeparationSettlementReadinessDto>> GetReadinessAsync(Guid separationId, CancellationToken ct = default)
    {
        var state = await EvaluateAsync(separationId, ct);
        return state.Result ?? Result<SeparationSettlementReadinessDto>.Success(ToReadiness(state));
    }

    public async Task<Result<SeparationSettlementStatusDto>> GetStatusAsync(Guid separationId, CancellationToken ct = default)
    {
        var state = await EvaluateAsync(separationId, ct);
        if (state.Result is not null) return Result<SeparationSettlementStatusDto>.Failure(state.Result.Status, state.Result.Message, state.Result.Errors);
        var orchestration = await db.SeparationSettlementOrchestrations.FirstOrDefaultAsync(x => x.TenantId == state.TenantId && x.EmployeeSeparationId == separationId, ct);
        var settlementStatus = await LoadPayrollStatusAsync(orchestration?.PayrollFinalSettlementId, state.TenantId, ct);
        if (orchestration is not null && settlementStatus == FinalSettlementStatus.Finalized && orchestration.Status != SeparationSettlementOrchestrationStatus.Completed)
        {
            var previous = orchestration.Status;
            orchestration.Status = SeparationSettlementOrchestrationStatus.Completed;
            orchestration.CompletedAtUtc ??= clock.GetUtcNow().UtcDateTime;
            orchestration.ConcurrencyVersion++;
            AddEvent(orchestration, SeparationSettlementEventType.FinalSettlementCompleted, previous, orchestration.Status, "Payroll final settlement is finalized.");
            try { failureInjector?.BeforeCompletionSyncCommit(); await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Result<SeparationSettlementStatusDto>.Conflict("Settlement status changed concurrently."); }
            catch (InvalidOperationException) { return Result<SeparationSettlementStatusDto>.Unavailable("Settlement completion synchronization can be retried."); }
        }
        var readyForClosure = settlementStatus == FinalSettlementStatus.Finalized && state.Blockers.Count == 0;
        return Result<SeparationSettlementStatusDto>.Success(ToStatus(separationId, orchestration, settlementStatus, readyForClosure, state.Blockers));
    }

    public async Task<Result<SeparationSettlementStatusDto>> InitiateAsync(Guid separationId, SettlementInitiationRequest request, CancellationToken ct = default)
    {
        var state = await EvaluateAsync(separationId, ct);
        if (state.Result is not null) return Result<SeparationSettlementStatusDto>.Failure(state.Result.Status, state.Result.Message, state.Result.Errors);
        if (state.Separation?.Status == EmployeeSeparationStatus.Closed) return Result<SeparationSettlementStatusDto>.Conflict("SeparationAlreadyClosed");
        if (request.ExpectedConcurrencyVersion is int expected && state.Separation!.ConcurrencyVersion != expected)
            return Result<SeparationSettlementStatusDto>.Conflict("The separation was changed concurrently.");
        if (state.Blockers.Count > 0) return Result<SeparationSettlementStatusDto>.Conflict("Settlement readiness has blocking items.", state.Blockers.Select(x => new ValidationError(x.Code, x.Message)).ToList());

        var orchestration = await db.SeparationSettlementOrchestrations.FirstOrDefaultAsync(x => x.TenantId == state.TenantId && x.EmployeeSeparationId == separationId, ct);
        if (orchestration?.PayrollFinalSettlementId is Guid linked)
            return await GetStatusAsync(separationId, ct);
        if (orchestration is null)
        {
            orchestration = new SeparationSettlementOrchestration { Id = Guid.NewGuid(), TenantId = state.TenantId, EmployeeSeparationId = separationId, EmployeeId = state.Separation.EmployeeId, IdempotencyKey = request.IdempotencyKey?.Trim() };
            db.SeparationSettlementOrchestrations.Add(orchestration);
        }
        CaptureSnapshot(orchestration, state);
        orchestration.Status = SeparationSettlementOrchestrationStatus.Initiated;
        orchestration.InitiatedAtUtc ??= clock.GetUtcNow().UtcDateTime;
        orchestration.InitiatedByUserId ??= tenant.UserId;
        orchestration.ReadinessCheckedAtUtc = clock.GetUtcNow().UtcDateTime;
        orchestration.ConcurrencyVersion++;
        AddEvent(orchestration, SeparationSettlementEventType.FinalSettlementInitiationRequested, SeparationSettlementOrchestrationStatus.Ready, orchestration.Status, "Final settlement initiation requested.");
        try { failureInjector?.BeforeInitiationCommit(); await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return await ReconcileAfterConcurrentInitiationAsync(state, ct); }
        catch (DbUpdateException) { return await ReconcileAfterConcurrentInitiationAsync(state, ct); }
        catch (InvalidOperationException) { return Result<SeparationSettlementStatusDto>.Unavailable("Settlement initiation was rolled back; retry is safe."); }

        var existing = await FindExistingSettlementAsync(state, ct);
        var settlement = existing is not null
            ? Result<FinalSettlementDto>.Success(ToSettlementDto(existing))
            : await payrollSettlement.CreateSettlementAsync(new FinalSettlementRequest { EmployeeId = state.Separation.EmployeeId, SeparationDate = state.Separation.ApprovedLastWorkingDate!.Value, LastWorkingDate = state.Separation.ApprovedLastWorkingDate.Value, SettlementDate = MaxDate(DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), state.Separation.ApprovedLastWorkingDate.Value), SeparationReason = MapReason(state.Separation.Reason?.Category) }, ct);
        if (!settlement.Succeeded)
        {
            orchestration.Status = SeparationSettlementOrchestrationStatus.Failed;
            orchestration.FailedAtUtc = clock.GetUtcNow().UtcDateTime;
            orchestration.LastFailureCode = settlement.Status.ToString();
            orchestration.LastFailureMessage = settlement.Message;
            orchestration.ConcurrencyVersion++;
            AddEvent(orchestration, SeparationSettlementEventType.FinalSettlementFailed, SeparationSettlementOrchestrationStatus.Initiated, orchestration.Status, settlement.Message);
            await db.SaveChangesAsync(ct);
            return Result<SeparationSettlementStatusDto>.Failure(settlement.Status, settlement.Message, settlement.Errors);
        }
        orchestration.PayrollFinalSettlementId = settlement.Value!.Id;
        orchestration.Status = SeparationSettlementOrchestrationStatus.Initiated;
        orchestration.ConcurrencyVersion++;
        AddEvent(orchestration, SeparationSettlementEventType.FinalSettlementLinked, SeparationSettlementOrchestrationStatus.Initiated, orchestration.Status, $"Payroll Final Settlement {settlement.Value.Id} linked.");
        try { failureInjector?.BeforePayrollLinkCommit(); await db.SaveChangesAsync(ct); }
        catch (InvalidOperationException) { return Result<SeparationSettlementStatusDto>.Unavailable("Payroll link persistence failed; reconciliation is safe."); }
        return await GetStatusAsync(separationId, ct);
    }

    private async Task<Result<SeparationSettlementStatusDto>> ReconcileAfterConcurrentInitiationAsync(Evaluation state, CancellationToken ct)
    {
        db.ClearChangeTracker();
        var existing = await db.SeparationSettlementOrchestrations.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == state.TenantId && x.EmployeeSeparationId == state.Separation!.Id, ct);
        return existing is null ? Result<SeparationSettlementStatusDto>.Conflict("Settlement initiation changed concurrently; retry with a fresh state.") : await GetStatusAsync(state.Separation.Id, ct);
    }

    public async Task<Result<SeparationSettlementStatusDto>> RetryAsync(Guid separationId, SettlementRetryRequest request, CancellationToken ct = default)
    {
        var current = await GetStatusAsync(separationId, ct);
        if (!current.Succeeded || current.Value is null) return current;
        if (current.Value.PayrollSettlementStatus == FinalSettlementStatus.Finalized) return Result<SeparationSettlementStatusDto>.Conflict("A finalized settlement cannot be retried.");
        var state = await EvaluateAsync(separationId, ct);
        if (state.Result is not null) return Result<SeparationSettlementStatusDto>.Failure(state.Result.Status, state.Result.Message, state.Result.Errors);
        var orchestration = await db.SeparationSettlementOrchestrations.SingleAsync(x => x.TenantId == state.TenantId && x.EmployeeSeparationId == separationId, ct);
        if (orchestration.PayrollFinalSettlementId is Guid existing)
        {
            orchestration.Status = SeparationSettlementOrchestrationStatus.Initiated;
            orchestration.LastFailureCode = null;
            orchestration.LastFailureMessage = null;
            orchestration.ConcurrencyVersion++;
            AddEvent(orchestration, SeparationSettlementEventType.FinalSettlementRetryRequested, SeparationSettlementOrchestrationStatus.Failed, orchestration.Status, request.Reason);
            await db.SaveChangesAsync(ct);
            return await GetStatusAsync(separationId, ct);
        }
        return await InitiateAsync(separationId, new SettlementInitiationRequest { IdempotencyKey = orchestration.IdempotencyKey }, ct);
    }

    public async Task<Result<IReadOnlyList<SeparationSettlementEventDto>>> GetHistoryAsync(Guid separationId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<SeparationSettlementEventDto>>.Unauthorized("No authenticated tenant.");
        var orchestration = await db.SeparationSettlementOrchestrations.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeSeparationId == separationId, ct);
        if (orchestration is null) return Result<IReadOnlyList<SeparationSettlementEventDto>>.NotFound("Settlement orchestration not found.");
        var events = await db.SeparationSettlementEvents.AsNoTracking().Where(x => x.TenantId == tenantId && x.SeparationSettlementOrchestrationId == orchestration.Id).OrderBy(x => x.OccurredAtUtc).Select(x => new SeparationSettlementEventDto(x.EventType, x.FromStatus, x.ToStatus, x.ActorUserId, x.OccurredAtUtc, x.Reason)).ToListAsync(ct);
        return Result<IReadOnlyList<SeparationSettlementEventDto>>.Success(events);
    }

    public async Task<Result<PagedResult<SeparationSettlementDashboardItemDto>>> GetDashboardAsync(SettlementDashboardQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<SeparationSettlementDashboardItemDto>>.Unauthorized("No authenticated tenant.");
        var baseQuery = db.SeparationSettlementOrchestrations.AsNoTracking().Include(x => x.EmployeeSeparation).ThenInclude(x => x!.Employee).Where(x => x.TenantId == tenantId);
        if (Enum.TryParse<SeparationSettlementOrchestrationStatus>(query.Status, true, out var status)) baseQuery = baseQuery.Where(x => x.Status == status);
        if (query.LwdFrom is DateOnly from) baseQuery = baseQuery.Where(x => x.EmployeeSeparation!.ApprovedLastWorkingDate >= from);
        if (query.LwdTo is DateOnly to) baseQuery = baseQuery.Where(x => x.EmployeeSeparation!.ApprovedLastWorkingDate <= to);
        if (!string.IsNullOrWhiteSpace(query.EmployeeSearch)) { var search = query.EmployeeSearch.Trim(); baseQuery = baseQuery.Where(x => (x.EmployeeSeparation!.Employee!.EmployeeCode ?? "").Contains(search) || (x.EmployeeSeparation.Employee.FirstName + " " + x.EmployeeSeparation.Employee.LastName).Contains(search)); }
        var total = await baseQuery.CountAsync(ct);
        var rows = await baseQuery.OrderByDescending(x => x.InitiatedAtUtc).Skip((Math.Max(1, query.Page) - 1) * Math.Clamp(query.PageSize, 1, 100)).Take(Math.Clamp(query.PageSize, 1, 100)).Select(x => new { x, EmployeeCode = x.EmployeeSeparation!.Employee!.EmployeeCode, EmployeeName = x.EmployeeSeparation.Employee.FirstName + " " + x.EmployeeSeparation.Employee.LastName }).ToListAsync(ct);
        var items = new List<SeparationSettlementDashboardItemDto>();
        foreach (var row in rows) { var readiness = await EvaluateAsync(row.x.EmployeeSeparationId, ct); var payrollStatus = await LoadPayrollStatusAsync(row.x.PayrollFinalSettlementId, tenantId, ct); items.Add(new(row.x.EmployeeSeparationId, row.x.EmployeeId, row.EmployeeCode, row.EmployeeName, row.x.ApprovedLastWorkingDateSnapshot, row.x.Status, payrollStatus, readiness.Blockers.Count == 0, readiness.Blockers.Count, row.x.InitiatedAtUtc, row.x.CompletedAtUtc, payrollStatus == FinalSettlementStatus.Finalized && readiness.Blockers.Count == 0)); }
        return Result<PagedResult<SeparationSettlementDashboardItemDto>>.Success(new PagedResult<SeparationSettlementDashboardItemDto>(items, Math.Max(1, query.Page), Math.Clamp(query.PageSize, 1, 100), total));
    }

    private async Task<Evaluation> EvaluateAsync(Guid separationId, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return new(null, Guid.Empty, null, [], null, null, 0, 0, Result<SeparationSettlementReadinessDto>.Unauthorized("No authenticated tenant."));
        var separation = await db.EmployeeSeparations.Include(x => x.Reason).AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == separationId, ct);
        if (separation is null) return new(null, tenantId, null, [], null, null, 0, 0, Result<SeparationSettlementReadinessDto>.NotFound("Separation case not found."));
        var blockers = new List<SettlementBlockerDto>();
        if (separation.Status is not EmployeeSeparationStatus.Approved and not EmployeeSeparationStatus.NoticePeriod and not EmployeeSeparationStatus.ReadyForExit) blockers.Add(new("SeparationNotApproved", "The separation is not approved."));
        if (separation.ApprovedLastWorkingDate is null) blockers.Add(new("MissingApprovedLwd", "An approved last working date is required."));
        if (separation.NoticePeriodDays is null || separation.NoticeServedDays is null || separation.NoticeShortfallDays is null || separation.NoticeStartDate is null) blockers.Add(new("NoticeNotFinalized", "Notice facts are incomplete."));
        var clearance = await db.SeparationClearances.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeSeparationId == separationId, ct);
        if (clearance is null || clearance.Status != SeparationClearanceStatus.Completed) blockers.Add(new("ClearanceIncomplete", "Clearance is not completed."));
        var pendingAssets = clearance is null ? 0 : await db.SeparationClearanceTasks.Where(x => x.TenantId == tenantId && x.SeparationClearanceId == clearance.Id).SelectMany(x => x.Assets).CountAsync(x => x.ReturnStatus == SeparationAssetReturnStatus.PendingReturn, ct);
        if (pendingAssets > 0) blockers.Add(new("PendingAssetReturn", "One or more assets are still pending return."));
        var recoveryAssets = clearance is null ? 0 : await db.SeparationClearanceTasks.Where(x => x.TenantId == tenantId && x.SeparationClearanceId == clearance.Id).SelectMany(x => x.Assets).CountAsync(x => x.RecoveryRequired, ct);
        var interview = await db.SeparationExitInterviews.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeSeparationId == separationId && x.Status != SeparationExitInterviewStatus.Cancelled, ct);
        if (interview is null || interview.Status is not SeparationExitInterviewStatus.Completed and not SeparationExitInterviewStatus.CompletedWithoutEmployeeResponse) blockers.Add(new("ExitInterviewIncomplete", "Exit interview must be completed, waived, or recorded as non-participation."));
        if (!await db.Employees.AnyAsync(x => x.TenantId == tenantId && x.Id == separation.EmployeeId, ct)) blockers.Add(new("EmployeeIdentityMissing", "Authoritative employee identity is missing."));
        var existing = separation.ApprovedLastWorkingDate is DateOnly lwd ? await db.FinalSettlementCases.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == separation.EmployeeId && x.SeparationDate == lwd && x.Status != FinalSettlementStatus.Cancelled, ct) : null;
        var readiness = new Evaluation(separation, tenantId, clearance, blockers, interview, existing, recoveryAssets, pendingAssets, null);
        return readiness;
    }

    private async Task<FinalSettlementCase?> FindExistingSettlementAsync(Evaluation state, CancellationToken ct) => state.ExistingSettlement ?? (state.Separation!.ApprovedLastWorkingDate is DateOnly lwd ? await db.FinalSettlementCases.FirstOrDefaultAsync(x => x.TenantId == state.TenantId && x.EmployeeId == state.Separation.EmployeeId && x.SeparationDate == lwd && x.Status != FinalSettlementStatus.Cancelled, ct) : null);
    private async Task<FinalSettlementStatus?> LoadPayrollStatusAsync(Guid? id, Guid tenantId, CancellationToken ct) => id is Guid settlementId ? await db.FinalSettlementCases.Where(x => x.TenantId == tenantId && x.Id == settlementId).Select(x => (FinalSettlementStatus?)x.Status).SingleOrDefaultAsync(ct) : null;
    private void CaptureSnapshot(SeparationSettlementOrchestration row, Evaluation state) { var separation = state.Separation!; row.ApprovedLastWorkingDateSnapshot = separation.ApprovedLastWorkingDate; row.NoticeStartDateSnapshot = separation.NoticeStartDate; row.RequiredNoticeDaysSnapshot = separation.NoticePeriodDays; row.ServedNoticeDaysSnapshot = separation.NoticeServedDays; row.WaivedNoticeDaysSnapshot = separation.WaivedNoticeDays; row.NoticeShortfallDaysSnapshot = separation.NoticeShortfallDays; row.ClearanceIdSnapshot = state.Clearance?.Id; row.ClearanceCompletedAtSnapshotUtc = state.Clearance?.CompletedAtUtc; row.PendingAssetRecoveryCountSnapshot = state.RecoveryAssets; row.ExitInterviewIdSnapshot = state.Interview?.Id; row.ExitInterviewDispositionSnapshot = state.Interview?.Status.ToString(); }
    private void AddEvent(SeparationSettlementOrchestration row, SeparationSettlementEventType type, SeparationSettlementOrchestrationStatus? from, SeparationSettlementOrchestrationStatus? to, string? reason) => db.SeparationSettlementEvents.Add(new SeparationSettlementEvent { Id = Guid.NewGuid(), TenantId = row.TenantId, SeparationSettlementOrchestrationId = row.Id, EventType = type, FromStatus = from, ToStatus = to, ActorUserId = tenant.UserId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime, Reason = reason });
    private static DateOnly MaxDate(DateOnly first, DateOnly second) => first > second ? first : second;
    private static PayrollSeparationReason MapReason(SeparationReasonCategory? category) => category switch { SeparationReasonCategory.Resignation => PayrollSeparationReason.Resignation, SeparationReasonCategory.Retirement => PayrollSeparationReason.Retirement, SeparationReasonCategory.Termination => PayrollSeparationReason.Termination, SeparationReasonCategory.Redundancy => PayrollSeparationReason.Redundancy, SeparationReasonCategory.Death => PayrollSeparationReason.Death, SeparationReasonCategory.ContractEnd => PayrollSeparationReason.ContractEnd, _ => PayrollSeparationReason.Other };
    private static SeparationSettlementReadinessDto ToReadiness(Evaluation x) => new(x.Separation?.Id ?? Guid.Empty, x.Blockers.Count == 0, x.Blockers, x.Separation?.ApprovedLastWorkingDate, x.Clearance?.Status, x.Interview?.Status, x.Separation?.NoticeShortfallDays, x.RecoveryAssets, x.ExistingSettlement?.Id, x.ExistingSettlement?.Status);
    private static SeparationSettlementStatusDto ToStatus(Guid separationId, SeparationSettlementOrchestration? row, FinalSettlementStatus? payrollStatus, bool ready, IReadOnlyList<SettlementBlockerDto> blockers) => new(separationId, row?.Id, row?.Status ?? SeparationSettlementOrchestrationStatus.NotReady, row?.PayrollFinalSettlementId, payrollStatus, row?.InitiatedAtUtc, row?.CompletedAtUtc, row?.LastFailureCode, row?.LastFailureMessage, ready, blockers);
    private static FinalSettlementDto ToSettlementDto(FinalSettlementCase x) => new(x.Id, x.EmployeeId, x.SeparationDate, x.LastWorkingDate, x.SettlementDate, x.SeparationReason, x.Status, x.GrossPayable, x.TotalDeductions, x.NetSettlement, x.CurrencyCode, x.Lines.OrderBy(l => l.Sequence).Select(l => new FinalSettlementLineDto(l.Id, l.LineType, l.ComponentCode, l.Description, l.Amount, l.IsEarning, l.IsDeduction, l.SourceType, l.SourceId)).ToList());
    private sealed record Evaluation(EmployeeSeparation? Separation, Guid TenantId, SeparationClearance? Clearance, List<SettlementBlockerDto> Blockers, SeparationExitInterview? Interview, FinalSettlementCase? ExistingSettlement, int RecoveryAssets, int PendingAssets, Result<SeparationSettlementReadinessDto>? Result);
}
