using System.Text.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Separation;
using HRMS.Domain.Entities;
using HRMS.Domain.Entities.Separation;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HRMS.Application.Services;

public sealed class SeparationExitService : ISeparationExitService
{
    private readonly IHrmsDbContext db;
    private readonly ITenantContext tenant;
    private readonly TimeProvider clock;
    private readonly ILogger<SeparationExitService> logger;
    private readonly ISeparationExitFailureInjector? failureInjector;

    public SeparationExitService(IHrmsDbContext db, ITenantContext tenant, TimeProvider? clock = null, ILogger<SeparationExitService>? logger = null, ISeparationExitFailureInjector? failureInjector = null)
    {
        this.db = db; this.tenant = tenant; this.clock = clock ?? TimeProvider.System;
        this.logger = logger ?? LoggerFactory.Create(_ => { }).CreateLogger<SeparationExitService>(); this.failureInjector = failureInjector;
    }

    public async Task<Result<SeparationExitReadinessDto>> GetReadinessAsync(Guid separationId, CancellationToken ct = default)
    {
        var e = await EvaluateAsync(separationId, ct);
        return e.Result is not null ? Result<SeparationExitReadinessDto>.Failure(e.Result.Status, e.Result.Message, e.Result.Errors) : Result<SeparationExitReadinessDto>.Success(ToReadiness(e));
    }

    public async Task<Result<SeparationExitExecutionDto>> GetAsync(Guid separationId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<SeparationExitExecutionDto>.Unauthorized("An authenticated tenant is required.");
        var row = await db.SeparationExitExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeSeparationId == separationId, ct);
        return row is null ? Result<SeparationExitExecutionDto>.NotFound("Exit execution has not been started.") : Result<SeparationExitExecutionDto>.Success(ToDto(row));
    }

    public async Task<Result<SeparationExitExecutionDto>> ExecuteAsync(Guid separationId, SeparationExitExecuteRequest request, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId || tenant.UserId is not Guid actorId) return Result<SeparationExitExecutionDto>.Unauthorized("An authenticated tenant and user are required.");
        var evaluation = await EvaluateAsync(separationId, ct);
        if (evaluation.Result is not null) return Result<SeparationExitExecutionDto>.Failure(evaluation.Result.Status, evaluation.Result.Message, evaluation.Result.Errors);
        if (evaluation.Existing?.Status == SeparationExitExecutionStatus.Completed) return Result<SeparationExitExecutionDto>.Success(ToDto(evaluation.Existing), "Separation exit was already completed.");
        if (evaluation.Separation?.Status == EmployeeSeparationStatus.Closed) return Result<SeparationExitExecutionDto>.Conflict("SeparationAlreadyClosed");
        if (evaluation.Account?.UserId == actorId) return Result<SeparationExitExecutionDto>.Forbidden("An employee cannot execute their own separation exit.");
        if (request.ExpectedConcurrencyVersion > 0 && evaluation.Separation!.ConcurrencyVersion != request.ExpectedConcurrencyVersion) return Result<SeparationExitExecutionDto>.Conflict("The separation changed concurrently.");
        if (!evaluation.IsReady) return Result<SeparationExitExecutionDto>.Conflict("Exit execution is blocked.", evaluation.Blockers.Select(x => new ValidationError(x.Code, x.Message)).ToList());

        var row = evaluation.Existing ?? new SeparationExitExecution { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeSeparationId = separationId, EmployeeId = evaluation.Separation.EmployeeId };
        if (evaluation.Existing is null) db.SeparationExitExecutions.Add(row);
        row.Status = SeparationExitExecutionStatus.InProgress; row.FinalLastWorkingDate = evaluation.Separation.ApprovedLastWorkingDate; row.StartedAtUtc ??= clock.GetUtcNow().UtcDateTime; row.StartedByUserId ??= actorId; row.IdempotencyKey ??= request.IdempotencyKey;
        row.SnapshotJson ??= JsonSerializer.Serialize(new { evaluation.Separation.EmployeeId, EmployeeCode = evaluation.Separation.Employee?.EmployeeCode, SeparationId = separationId, FinalLastWorkingDate = evaluation.Separation.ApprovedLastWorkingDate, ManagerId = evaluation.Separation.Employee?.ReportingManagerId, Account = evaluation.Account?.UserId, CapturedAtUtc = clock.GetUtcNow().UtcDateTime });
        row.ConcurrencyVersion++; AddEvent(row, SeparationExitExecutionEventType.ExitExecutionStarted, "Execution", request.Comment); AddSeparationEvent(evaluation.Separation, EmployeeSeparationEventType.ExitExecutionStarted, "Exit execution started."); await db.SaveChangesAsync(ct);
        try
        {
            await ExecuteEmploymentAsync(row, evaluation, ct);
            await ExecuteAccessAsync(row, evaluation, ct);
            await ExecuteSessionsAsync(row, evaluation, ct);
            await ExecuteRolesAsync(row, evaluation, ct);
            AddEvent(row, SeparationExitExecutionEventType.ManagerResponsibilitiesReconciled, "ManagerResponsibilities", "Manager responsibilities reconciled."); AddSeparationEvent(evaluation.Separation, EmployeeSeparationEventType.ManagerResponsibilitiesReconciled, "Manager responsibilities reconciled."); await db.SaveChangesAsync(ct);
            failureInjector?.BeforeFinalClosure();
            row.Status = SeparationExitExecutionStatus.Completed; row.CompletedAtUtc = clock.GetUtcNow().UtcDateTime; row.FailureCode = null; row.FailureMessage = null; evaluation.Separation.Status = EmployeeSeparationStatus.Closed; evaluation.Separation.ConcurrencyVersion++;
            AddEvent(row, SeparationExitExecutionEventType.SeparationClosed, "Closure", "Separation closed."); AddSeparationEvent(evaluation.Separation, EmployeeSeparationEventType.SeparationClosed, "Separation closed after exit execution."); await db.SaveChangesAsync(ct);
            logger.LogInformation("Separation exit completed. TenantId {TenantId}, SeparationId {SeparationId}, EmployeeId {EmployeeId}, ExecutionId {ExecutionId}, Step {Step}, Result {Result}.", tenantId, separationId, row.EmployeeId, row.Id, "Closure", "Completed");
            return Result<SeparationExitExecutionDto>.Success(ToDto(row));
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException or DbUpdateConcurrencyException)
        {
            row.Status = SeparationExitExecutionStatus.Failed; row.FailedAtUtc = clock.GetUtcNow().UtcDateTime; row.FailureCode = FailureCode(exception); row.FailureMessage = SafeFailureMessage(exception);
            AddEvent(row, SeparationExitExecutionEventType.ExitExecutionFailed, "Execution", row.FailureMessage); AddSeparationEvent(evaluation.Separation, EmployeeSeparationEventType.ExitExecutionFailed, row.FailureMessage); await db.SaveChangesAsync(ct);
            return Result<SeparationExitExecutionDto>.Unavailable("Exit execution failed and is retryable.");
        }
    }

    public async Task<Result<SeparationExitExecutionDto>> RetryAsync(Guid separationId, SeparationExitRetryRequest request, CancellationToken ct = default)
    {
        var existing = await GetAsync(separationId, ct); if (!existing.Succeeded || existing.Value is null) return existing; if (existing.Value.Status == SeparationExitExecutionStatus.Completed) return existing;
        return await ExecuteAsync(separationId, new SeparationExitExecuteRequest(0, request.IdempotencyKey, request.Reason), ct);
    }

    public async Task<Result<IReadOnlyList<SeparationExitExecutionEventDto>>> HistoryAsync(Guid separationId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<IReadOnlyList<SeparationExitExecutionEventDto>>.Unauthorized("An authenticated tenant is required.");
        var execution = await db.SeparationExitExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeSeparationId == separationId, ct);
        if (execution is null) return Result<IReadOnlyList<SeparationExitExecutionEventDto>>.NotFound("Exit execution has not been started.");
        var events = await db.SeparationExitExecutionEvents.AsNoTracking().Where(x => x.TenantId == tenantId && x.SeparationExitExecutionId == execution.Id).OrderBy(x => x.OccurredAtUtc).Select(x => new SeparationExitExecutionEventDto(x.EventType, x.Step, x.OccurredAtUtc, x.ActorUserId, x.Reason)).ToListAsync(ct);
        return Result<IReadOnlyList<SeparationExitExecutionEventDto>>.Success(events);
    }

    public async Task<Result<PagedResult<SeparationExitDashboardItemDto>>> DashboardAsync(SeparationExitDashboardQuery query, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tenantId) return Result<PagedResult<SeparationExitDashboardItemDto>>.Unauthorized("An authenticated tenant is required.");
        var page = Math.Max(1, query.Page); var size = Math.Clamp(query.PageSize, 1, 100); var source = db.EmployeeSeparations.AsNoTracking().Include(x => x.Employee).Where(x => x.TenantId == tenantId);
        if (query.LwdFrom is DateOnly from) source = source.Where(x => x.ApprovedLastWorkingDate >= from); if (query.LwdTo is DateOnly to) source = source.Where(x => x.ApprovedLastWorkingDate <= to);
        if (!string.IsNullOrWhiteSpace(query.EmployeeSearch)) { var search = query.EmployeeSearch.Trim(); source = source.Where(x => (x.Employee!.EmployeeCode ?? "").Contains(search) || (x.Employee.FirstName + " " + x.Employee.LastName).Contains(search)); }
        if (Enum.TryParse<EmployeeSeparationStatus>(query.Status, true, out var separationStatus)) source = source.Where(x => x.Status == separationStatus);
        else if (Enum.TryParse<SeparationExitExecutionStatus>(query.Status, true, out var executionStatus))
        {
            var executionIds = db.SeparationExitExecutions.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status == executionStatus).Select(x => x.EmployeeSeparationId);
            source = source.Where(x => executionIds.Contains(x.Id));
        }
        var total = await source.CountAsync(ct); var rows = await source.OrderByDescending(x => x.ApprovedLastWorkingDate).Skip((page - 1) * size).Take(size).ToListAsync(ct); var items = new List<SeparationExitDashboardItemDto>();
        foreach (var separation in rows) { var e = await EvaluateAsync(separation.Id, ct); var execution = e.Existing; items.Add(new(separation.Id, separation.EmployeeId, separation.Employee?.EmployeeCode, $"{separation.Employee?.FirstName} {separation.Employee?.LastName}".Trim(), separation.ApprovedLastWorkingDate, separation.Status, execution?.Status ?? SeparationExitExecutionStatus.NotStarted, separation.Employee?.Status ?? EmployeeStatus.Active, e.IsReady, e.Blockers.Count, execution?.CompletedAtUtc)); }
        return Result<PagedResult<SeparationExitDashboardItemDto>>.Success(new(items, page, size, total));
    }

    private async Task ExecuteEmploymentAsync(SeparationExitExecution row, Evaluation e, CancellationToken ct)
    {
        if (row.EmploymentExecutedAtUtc is not null) return; failureInjector?.BeforeEmploymentExit();
        var employee = await db.Employees.SingleAsync(x => x.TenantId == row.TenantId && x.Id == row.EmployeeId, ct); if (employee.Status == EmployeeStatus.Active) { employee.DateOfLeaving = row.FinalLastWorkingDate; employee.Status = EmployeeStatus.Terminated; employee.JobStatus = "Separated"; }
        var finalLwd = row.FinalLastWorkingDate ?? throw new InvalidOperationException("Final last working date is required.");
        var current = await db.EmployeeEmploymentHistory.Where(x => x.TenantId == row.TenantId && x.EmployeeId == row.EmployeeId && !x.IsSuperseded && x.EffectiveFrom <= finalLwd && (x.EffectiveTo == null || x.EffectiveTo >= finalLwd)).ToListAsync(ct);
        foreach (var history in current) { history.EffectiveTo = finalLwd; history.EmploymentStatus = EmployeeStatus.Terminated; }
        row.EmploymentExecutedAtUtc = clock.GetUtcNow().UtcDateTime; AddEvent(row, SeparationExitExecutionEventType.EmploymentExitExecuted, "Employment", "Authoritative employment exit applied."); AddEvent(row, SeparationExitExecutionEventType.EmployeeInactivated, "Employment", "Employee marked inactive/separated."); AddSeparationEvent(e.Separation, EmployeeSeparationEventType.EmploymentExitExecuted, "Employment terminal transition applied."); await db.SaveChangesAsync(ct);
    }

    private async Task ExecuteAccessAsync(SeparationExitExecution row, Evaluation e, CancellationToken ct)
    {
        if (row.AccessDeprovisionedAtUtc is not null) return; failureInjector?.BeforeAccessDeprovision();
        if (e.Account is not null) { var user = await db.Users.SingleAsync(x => x.TenantId == row.TenantId && x.Id == e.Account.UserId, ct); user.IsActive = false; }
        row.AccessDeprovisionedAtUtc = clock.GetUtcNow().UtcDateTime; AddEvent(row, SeparationExitExecutionEventType.TenantAccessDeprovisioned, "Access", "Tenant account access deactivated."); AddSeparationEvent(e.Separation, EmployeeSeparationEventType.TenantAccessDeprovisioned, "Tenant access deprovisioned."); await db.SaveChangesAsync(ct);
    }

    private async Task ExecuteSessionsAsync(SeparationExitExecution row, Evaluation e, CancellationToken ct)
    {
        if (e.Account is null || await HasEventAsync(row.Id, SeparationExitExecutionEventType.SessionsRevoked, ct)) return; failureInjector?.BeforeSessionRevocation(); var now = clock.GetUtcNow().UtcDateTime;
        var activeTokens = await db.RefreshTokens.Where(x => x.TenantId == row.TenantId && x.UserId == e.Account.UserId && x.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var token in activeTokens) { token.RevokedAtUtc = now; token.ModifiedDate = now; }
        AddEvent(row, SeparationExitExecutionEventType.SessionsRevoked, "Sessions", "Active refresh sessions revoked."); AddSeparationEvent(e.Separation, EmployeeSeparationEventType.SessionsRevoked, "Sessions revoked."); await db.SaveChangesAsync(ct);
    }

    private async Task ExecuteRolesAsync(SeparationExitExecution row, Evaluation e, CancellationToken ct)
    {
        if (e.Account is null || await HasEventAsync(row.Id, SeparationExitExecutionEventType.RoleAssignmentsRevoked, ct)) return; failureInjector?.BeforeRoleReconciliation(); var end = row.FinalLastWorkingDate; var today = DateOnly.FromDateTime(clock.GetUtcNow().DateTime);
        var roles = await db.UserRoles.Include(x => x.Role).Where(x => x.TenantId == row.TenantId && x.UserId == e.Account.UserId && x.EffectiveFrom <= today && (x.EffectiveTo == null || x.EffectiveTo >= today)).ToListAsync(ct);
        foreach (var role in roles) { role.EffectiveTo = end; role.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime; db.UserRoleAssignmentEvents.Add(new UserRoleAssignmentEvent { Id = Guid.NewGuid(), TenantId = row.TenantId, AssignmentId = role.Id, UserId = role.UserId, RoleId = role.RoleId, EventType = UserRoleAssignmentEventType.Revoked, EffectiveFrom = role.EffectiveFrom, EffectiveTo = end, AssignmentSource = role.AssignmentSource, Reason = "Separation exit execution", PerformedByUserId = tenant.UserId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime }); }
        AddEvent(row, SeparationExitExecutionEventType.RoleAssignmentsRevoked, "Roles", "Tenant role assignments reconciled."); AddSeparationEvent(e.Separation, EmployeeSeparationEventType.RoleAssignmentsRevoked, "Role assignments revoked/deactivated."); await db.SaveChangesAsync(ct);
    }

    private async Task<Evaluation> EvaluateAsync(Guid separationId, CancellationToken ct)
    {
        if (tenant.TenantId is not Guid tenantId) return Evaluation.Failure(Result<SeparationExitReadinessDto>.Unauthorized("An authenticated tenant is required."));
        var separation = await db.EmployeeSeparations.Include(x => x.Employee).Include(x => x.Events).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == separationId, ct); if (separation is null) return Evaluation.Failure(Result<SeparationExitReadinessDto>.NotFound("Separation not found."));
        var existing = await db.SeparationExitExecutions.Include(x => x.Events).FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeSeparationId == separationId, ct); var blockers = new List<SeparationExitBlockerDto>(); var businessDate = DateOnly.FromDateTime(clock.GetUtcNow().DateTime);
        if (separation.Status == EmployeeSeparationStatus.Closed) return new(separation, existing, null, blockers, businessDate, true, null);
        if (separation.Status is not EmployeeSeparationStatus.Approved and not EmployeeSeparationStatus.NoticePeriod and not EmployeeSeparationStatus.ReadyForExit) blockers.Add(new("SeparationNotApproved", "Separation is not approved for exit."));
        if (separation.ApprovedLastWorkingDate is not DateOnly lwd) blockers.Add(new("MissingFinalLwd", "A final approved last working date is required.")); else if (businessDate < lwd) blockers.Add(new("LwdNotReached", "Exit execution is not permitted before the final last working date."));
        if (separation.NoticeStartDate is null || separation.NoticePeriodDays is null || separation.NoticeServedDays is null || separation.NoticeShortfallDays is null) blockers.Add(new("NoticeNotFinalized", "Notice facts are incomplete."));
        var clearance = await db.SeparationClearances.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeSeparationId == separationId, ct); if (clearance is null || clearance.Status != SeparationClearanceStatus.Completed) blockers.Add(new("ClearanceIncomplete", "Clearance is not complete."));
        if (clearance is not null && await db.SeparationClearanceTasks.Where(x => x.TenantId == tenantId && x.SeparationClearanceId == clearance.Id).SelectMany(x => x.Assets).AnyAsync(x => x.ReturnStatus == SeparationAssetReturnStatus.PendingReturn, ct)) blockers.Add(new("AssetReturnPending", "Assets remain pending return."));
        var interview = await db.SeparationExitInterviews.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeSeparationId == separationId && x.Status != SeparationExitInterviewStatus.Cancelled, ct); if (interview is null || interview.Status is not SeparationExitInterviewStatus.Completed and not SeparationExitInterviewStatus.CompletedWithoutEmployeeResponse) blockers.Add(new("ExitInterviewIncomplete", "Exit interview is not complete or waived."));
        var settlement = await db.SeparationSettlementOrchestrations.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeSeparationId == separationId, ct); var finalSettlement = settlement?.PayrollFinalSettlementId is Guid sid ? await db.FinalSettlementCases.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == sid, ct) : null; if (settlement?.Status != SeparationSettlementOrchestrationStatus.Completed || finalSettlement?.Status != FinalSettlementStatus.Finalized) blockers.Add(new("FinalSettlementIncomplete", "Final settlement is not finalized."));
        if (existing?.EmploymentExecutedAtUtc is null && (separation.Employee is null || separation.Employee.Status != EmployeeStatus.Active)) blockers.Add(new("EmploymentStateInvalid", "Employee is not in an active executable state."));
        if (await db.Employees.AnyAsync(x => x.TenantId == tenantId && x.ReportingManagerId == separation.EmployeeId && x.Status == EmployeeStatus.Active, ct)) blockers.Add(new("ManagerReassignmentRequired", "Active direct reports still resolve to this employee."));
        var account = await db.AccountEmployeeCurrentLinks.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.EmployeeId == separation.EmployeeId, ct);
        return new(separation, existing, account, blockers, businessDate, blockers.Count == 0, null);
    }

    private async Task<bool> HasEventAsync(Guid executionId, SeparationExitExecutionEventType type, CancellationToken ct) => await db.SeparationExitExecutionEvents.AnyAsync(x => x.SeparationExitExecutionId == executionId && x.EventType == type, ct);
    private void AddEvent(SeparationExitExecution row, SeparationExitExecutionEventType type, string step, string? reason) { if (!db.SeparationExitExecutionEvents.Local.Any(x => x.SeparationExitExecutionId == row.Id && x.EventType == type)) db.SeparationExitExecutionEvents.Add(new SeparationExitExecutionEvent { Id = Guid.NewGuid(), TenantId = row.TenantId, SeparationExitExecutionId = row.Id, EventType = type, ActorUserId = tenant.UserId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime, Step = step, Reason = reason }); }
    private void AddSeparationEvent(EmployeeSeparation separation, EmployeeSeparationEventType type, string reason) { if (!db.EmployeeSeparationEvents.Local.Any(x => x.EmployeeSeparationId == separation.Id && x.EventType == type)) db.EmployeeSeparationEvents.Add(new EmployeeSeparationEvent { Id = Guid.NewGuid(), TenantId = separation.TenantId, EmployeeSeparationId = separation.Id, EventType = type, ActorUserId = tenant.UserId, OccurredAtUtc = clock.GetUtcNow().UtcDateTime, Reason = reason }); }
    private static string FailureCode(Exception exception) => exception switch { DbUpdateConcurrencyException => "Concurrency", DbUpdateException => "Persistence", _ => "Execution" };
    private static string SafeFailureMessage(Exception exception) => exception is InvalidOperationException ? exception.Message : "A closure step failed; retry is available.";
    private static SeparationExitReadinessDto ToReadiness(Evaluation e) => new(e.Separation?.Id ?? Guid.Empty, e.IsReady, e.Blockers, e.Separation?.ApprovedLastWorkingDate, e.BusinessDate, e.Existing?.Status ?? SeparationExitExecutionStatus.NotStarted);
    private static SeparationExitExecutionDto ToDto(SeparationExitExecution x) => new(x.Id, x.EmployeeSeparationId, x.EmployeeId, x.Status, x.FinalLastWorkingDate, x.StartedAtUtc, x.EmploymentExecutedAtUtc, x.AccessDeprovisionedAtUtc, x.CompletedAtUtc, x.FailureCode, x.FailureMessage, x.ConcurrencyVersion);

    private sealed record Evaluation(EmployeeSeparation? Separation, SeparationExitExecution? Existing, AccountEmployeeCurrentLink? Account, List<SeparationExitBlockerDto> Blockers, DateOnly BusinessDate, bool IsReady, Result<SeparationExitReadinessDto>? Result)
    { public static Evaluation Failure(Result<SeparationExitReadinessDto> result) => new(null, null, null, [], DateOnly.MinValue, false, result); }
}
