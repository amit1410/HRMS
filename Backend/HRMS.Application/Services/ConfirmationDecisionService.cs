using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Application.DTOs.Employees;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Phase 9c-3 decision engine. Scores a completed review case against its immutable question/rating snapshot and
/// the (frozen) policy version's configured decision rules, and — on an authorized submission — applies the
/// terminal outcome with concurrency protection and full audit. Employee data is never touched until a final
/// decision is applied, and a case is decided at most once per cycle (guaranteed by a compare-and-swap on the
/// case status). A probation extension creates a brand-new probation cycle (preserving the prior one) so the
/// Phase 9b scheduler, whose dedup is keyed on the probation-end date, re-notifies for the new cycle.
/// </summary>
public sealed class ConfirmationDecisionService : IConfirmationDecisionService
{
    private const string NoTenant = "No authenticated tenant.";
    private const string CaseNotFound = "Review case not found.";
    private const string NotAuthorized = "You are not authorized to act on this review case.";
    private const string DefaultConfirmedStatus = "Confirmed";
    private const string DefaultNotConfirmedStatus = "Not Confirmed";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IEmployeeAccessScopeService _accessScope;
    private readonly IConfirmationReviewCaseService _caseService;
    private readonly IJobStatusService _jobStatusService;
    private readonly TimeProvider _time;

    public ConfirmationDecisionService(
        IHrmsDbContext db,
        ITenantContext tenant,
        IEmployeeAccessScopeService accessScope,
        IConfirmationReviewCaseService caseService,
        IJobStatusService jobStatusService,
        TimeProvider? time = null)
    {
        _db = db;
        _tenant = tenant;
        _accessScope = accessScope;
        _caseService = caseService;
        _jobStatusService = jobStatusService;
        _time = time ?? TimeProvider.System;
    }

    private DateOnly Today => DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

    public async Task<Result<ConfirmationDecisionPreviewDto>> CalculateAsync(Guid caseId, CancellationToken ct = default, int? reviewRound = null)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationDecisionPreviewDto>.Unauthorized(NoTenant);
        var reviewCaseQuery = _db.ConfirmationReviewCases.AsNoTracking().Where(c => c.Id == caseId);

        // If reviewRound is specified, filter by it; otherwise get the current active round
        if (reviewRound.HasValue)
        {
            reviewCaseQuery = reviewCaseQuery.Where(c => c.ReviewRound == reviewRound.Value);
        }

        var reviewCase = await reviewCaseQuery.FirstOrDefaultAsync(ct);
        if (reviewCase is null) return Result<ConfirmationDecisionPreviewDto>.NotFound(CaseNotFound);
        if (!await _accessScope.CanAccessEmployeeAsync(reviewCase.EmployeeId, Today, ct))
            return Result<ConfirmationDecisionPreviewDto>.Forbidden(NotAuthorized);

        var context = await BuildEvaluationContextAsync(reviewCase, ct);
        var preview = context.Preview;

        // Every preview is recorded so the calculation that backed a later decision is reproducible.
        var now = _time.GetUtcNow().UtcDateTime;
        _db.ConfirmationDecisionCalculations.Add(new ConfirmationDecisionCalculation
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewCaseId = caseId,
            OverallRating = preview.OverallRating, AppliedRuleId = preview.AppliedRuleId,
            CalculatedOutcome = preview.CalculatedOutcome, Explanation = Truncate(preview.Explanation, 4000),
            ActorUserId = _tenant.UserId, CalculatedAtUtc = now, CreatedDate = now,
            ReviewRound = reviewCase.ReviewRound // Store the review round with the calculation
        });
        _db.ConfirmationReviewCaseEvents.Add(Event(tenantId, caseId, ConfirmationReviewCaseEventType.DecisionCalculated, now,
            $"Calculated overall rating {preview.OverallRating?.ToString() ?? "n/a"} → {preview.CalculatedOutcome} (Review Round {reviewCase.ReviewRound})."));
        await _db.SaveChangesAsync(ct);

        return Result<ConfirmationDecisionPreviewDto>.Success(preview);
    }

    public async Task<Result<ConfirmationDecisionResultDto>> SubmitDecisionAsync(Guid caseId, SubmitConfirmationDecisionRequest request, CancellationToken ct = default, int? reviewRound = null)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationDecisionResultDto>.Unauthorized(NoTenant);

        // Build query for the review case, optionally filtering by review round
        var reviewCaseQuery = _db.ConfirmationReviewCases.AsNoTracking().Where(c => c.Id == caseId);
        if (reviewRound.HasValue)
        {
            reviewCaseQuery = reviewCaseQuery.Where(c => c.ReviewRound == reviewRound.Value);
        }

        var reviewCase = await reviewCaseQuery.FirstOrDefaultAsync(ct);
        if (reviewCase is null) return Result<ConfirmationDecisionResultDto>.NotFound(CaseNotFound);
        if (!await _accessScope.CanAccessEmployeeAsync(reviewCase.EmployeeId, Today, ct))
            return Result<ConfirmationDecisionResultDto>.Forbidden(NotAuthorized);

        if (reviewCase.Status == ConfirmationReviewCaseStatus.Decided || reviewCase.Outcome != ConfirmationOutcome.Pending)
            return Result<ConfirmationDecisionResultDto>.Conflict("This case has already been decided.");

        var context = await BuildEvaluationContextAsync(reviewCase, ct);
        var preview = context.Preview;

        // Enforce readiness and the configured workflow server-side (never frontend-only).
        if (reviewCase.Status != ConfirmationReviewCaseStatus.Completed)
            return Result<ConfirmationDecisionResultDto>.Conflict("The required reviews have not been submitted, so a decision cannot be made yet.");
        if (preview.BlockingReasons.Count > 0)
            return Result<ConfirmationDecisionResultDto>.Invalid("A decision cannot be made yet.", preview.BlockingReasons.Select(r => new ValidationError("decision", r)).ToList());
        if (!preview.PermittedOutcomes.Contains(request.Outcome))
            return Result<ConfirmationDecisionResultDto>.Invalid("outcome", $"Outcome '{request.Outcome}' is not permitted by the configured decision rules/workflow for this case.");

        return request.Outcome switch
        {
            ConfirmationOutcome.Confirmed => await ConfirmAsync(tenantId, reviewCase, context, request, ct),
            ConfirmationOutcome.ProbationExtended => await ExtendAsync(tenantId, reviewCase, context, request, ct),
            ConfirmationOutcome.NotConfirmed => await RejectAsync(tenantId, reviewCase, context, request, ct),
            ConfirmationOutcome.AdditionalReview => await AdditionalReviewAsync(tenantId, reviewCase, context, request, ct),
            _ => Result<ConfirmationDecisionResultDto>.Invalid("outcome", "Unsupported decision outcome.")
        };
    }

    public async Task<Result<ConfirmationDecisionHistoryDto>> GetHistoryAsync(Guid caseId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<ConfirmationDecisionHistoryDto>.Unauthorized(NoTenant);
        var reviewCase = await _db.ConfirmationReviewCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caseId, ct);
        if (reviewCase is null) return Result<ConfirmationDecisionHistoryDto>.NotFound(CaseNotFound);
        if (!await _accessScope.CanAccessEmployeeAsync(reviewCase.EmployeeId, Today, ct))
            return Result<ConfirmationDecisionHistoryDto>.Forbidden(NotAuthorized);

        var decisions = await _db.ConfirmationDecisions.AsNoTracking()
            .Where(d => d.ConfirmationReviewCaseId == caseId)
            .OrderByDescending(d => d.DecidedAtUtc).ThenByDescending(d => d.Id)
            .Select(d => new ConfirmationDecisionDto(d.Id, d.ProbationCycleKey, d.Outcome, d.DecisionReason, d.DecisionMakerUserId, d.DecisionMakerEmployeeId,
                d.DecidedAtUtc, d.CalculatedOverallRating, d.AppliedRuleId, d.PreviousCaseStatus, d.NewCaseStatus, d.ReviewRound))
            .ToListAsync(ct);

        var calcs = await _db.ConfirmationDecisionCalculations.AsNoTracking()
            .Where(c => c.ConfirmationReviewCaseId == caseId)
            .OrderByDescending(c => c.CalculatedAtUtc).ThenByDescending(c => c.Id)
            .Select(c => new ConfirmationDecisionCalculationDto(c.Id, c.OverallRating, c.AppliedRuleId, c.CalculatedOutcome, c.Explanation, c.ActorUserId, c.CalculatedAtUtc, c.ReviewRound))
            .ToListAsync(ct);

        var extensions = await _db.ConfirmationProbationExtensions.AsNoTracking()
            .Where(e => e.ConfirmationReviewCaseId == caseId)
            .OrderByDescending(e => e.ExtendedAtUtc).ThenByDescending(e => e.Id)
            .Join(_db.ConfirmationProbationCycles.AsNoTracking(), e => e.ConfirmationProbationCycleId, c => c.Id, (e, c) => new { e, c.CycleNumber })
            .Select(x => new ConfirmationProbationExtensionDto(x.e.Id, x.CycleNumber, x.e.PreviousEndDate, x.e.ExtensionDuration, x.e.ExtensionDurationUnit, x.e.NewEndDate, x.e.DecisionMakerUserId, x.e.Reason, x.e.ExtendedAtUtc))
            .ToListAsync(ct);

        return Result<ConfirmationDecisionHistoryDto>.Success(new ConfirmationDecisionHistoryDto(caseId, decisions, calcs, extensions));
    }

    // ── outcome application ─────────────────────────────────────────────────

    private async Task<Result<ConfirmationDecisionResultDto>> ConfirmAsync(Guid tenantId, ConfirmationReviewCase reviewCase, EvaluationContext context, SubmitConfirmationDecisionRequest request, CancellationToken ct)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == reviewCase.EmployeeId, ct);
        if (employee is null) return Result<ConfirmationDecisionResultDto>.NotFound("Employee not found.");

        var confirmationDate = request.ConfirmationDateOverride ?? reviewCase.ProbationEndDate;
        if (confirmationDate < employee.DateOfJoining)
            return Result<ConfirmationDecisionResultDto>.Invalid("confirmationDateOverride", "Confirmation date cannot be before the date of joining.");
        var jobStatus = Normalize(context.PolicyRule?.ConfirmedJobStatus) ?? DefaultConfirmedStatus;

        var now = _time.GetUtcNow().UtcDateTime;
        await using var tx = await _db.BeginTransactionAsync(ct);

        if (!await CompareAndSwapToDecidedAsync(reviewCase.Id, ConfirmationOutcome.Confirmed, now, ct))
            return Result<ConfirmationDecisionResultDto>.Conflict("This case has already been decided.");

        var statusResult = await _jobStatusService.ChangeWithinTransactionAsync(
            new(reviewCase.EmployeeId, jobStatus, JobStatusMutationSource.Confirmation, request.DecisionReason), tx, ct);
        if (!statusResult.Succeeded)
            return Result<ConfirmationDecisionResultDto>.Failure(statusResult.Status, statusResult.Message, statusResult.Errors);

        // The confirmation workflow is the authorized writer of policy-managed confirmation data (the manual
        // employment upsert path intentionally rejects such edits); DateOfJoining is never touched. Job Status
        // fields and their Employee audit row were persisted above through the caller-owned transaction.
        var employment = await GetOrCreateEmploymentAsync(tenantId, reviewCase.EmployeeId, now, ct);
        employment.ConfirmationDate = confirmationDate;
        employment.ModifiedDate = now;
        employee.ModifiedDate = now;

        await CloseActiveCycleAsync(reviewCase.EmployeeId, now, ct);

        var decision = AddDecision(tenantId, reviewCase, context, ConfirmationOutcome.Confirmed, request.DecisionReason,
            ConfirmationReviewCaseStatus.Completed, ConfirmationReviewCaseStatus.Decided, now, context.DeciderEmployeeId, reviewCase.ReviewRound);
        _db.ConfirmationReviewCaseEvents.Add(Event(tenantId, reviewCase.Id, ConfirmationReviewCaseEventType.DecisionSubmitted, now, $"Decision submitted: Confirmed (rating {context.Preview.OverallRating?.ToString() ?? "n/a"})."));
        _db.ConfirmationReviewCaseEvents.Add(Event(tenantId, reviewCase.Id, ConfirmationReviewCaseEventType.EmployeeConfirmed, now, $"Employee confirmed; confirmation date {confirmationDate}, job status '{jobStatus}'."));

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Result<ConfirmationDecisionResultDto>.Success(new ConfirmationDecisionResultDto(
            decision.Id, reviewCase.Id, ConfirmationOutcome.Confirmed, ConfirmationReviewCaseStatus.Completed, ConfirmationReviewCaseStatus.Decided,
            context.Preview.OverallRating, context.Preview.AppliedRuleId, now, confirmationDate, jobStatus, null, null, null, null, reviewCase.ReviewRound, "Employee confirmed."), "Employee confirmed.");
    }

    private async Task<Result<ConfirmationDecisionResultDto>> RejectAsync(Guid tenantId, ConfirmationReviewCase reviewCase, EvaluationContext context, SubmitConfirmationDecisionRequest request, CancellationToken ct)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == reviewCase.EmployeeId, ct);
        if (employee is null) return Result<ConfirmationDecisionResultDto>.NotFound("Employee not found.");
        var jobStatus = Normalize(context.PolicyRule?.NotConfirmedJobStatus) ?? DefaultNotConfirmedStatus;

        var now = _time.GetUtcNow().UtcDateTime;
        await using var tx = await _db.BeginTransactionAsync(ct);

        if (!await CompareAndSwapToDecidedAsync(reviewCase.Id, ConfirmationOutcome.NotConfirmed, now, ct))
            return Result<ConfirmationDecisionResultDto>.Conflict("This case has already been decided.");

        var statusResult = await _jobStatusService.ChangeWithinTransactionAsync(
            new(reviewCase.EmployeeId, jobStatus, JobStatusMutationSource.Confirmation, request.DecisionReason), tx, ct);
        if (!statusResult.Succeeded)
            return Result<ConfirmationDecisionResultDto>.Failure(statusResult.Status, statusResult.Message, statusResult.Errors);

        // Reject is a configurable job-status outcome only; Employee.Status / termination stays with Separation.
        var employment = await GetOrCreateEmploymentAsync(tenantId, reviewCase.EmployeeId, now, ct);
        employment.ModifiedDate = now;
        employee.ModifiedDate = now;

        await CloseActiveCycleAsync(reviewCase.EmployeeId, now, ct);

        var decision = AddDecision(tenantId, reviewCase, context, ConfirmationOutcome.NotConfirmed, request.DecisionReason,
            ConfirmationReviewCaseStatus.Completed, ConfirmationReviewCaseStatus.Decided, now, context.DeciderEmployeeId, reviewCase.ReviewRound);
        _db.ConfirmationReviewCaseEvents.Add(Event(tenantId, reviewCase.Id, ConfirmationReviewCaseEventType.DecisionSubmitted, now, $"Decision submitted: Not Confirmed (rating {context.Preview.OverallRating?.ToString() ?? "n/a"})."));
        _db.ConfirmationReviewCaseEvents.Add(Event(tenantId, reviewCase.Id, ConfirmationReviewCaseEventType.MarkedNotConfirmed, now, $"Employee not confirmed; job status '{jobStatus}'."));

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Result<ConfirmationDecisionResultDto>.Success(new ConfirmationDecisionResultDto(
            decision.Id, reviewCase.Id, ConfirmationOutcome.NotConfirmed, ConfirmationReviewCaseStatus.Completed, ConfirmationReviewCaseStatus.Decided,
            context.Preview.OverallRating, context.Preview.AppliedRuleId, now, null, jobStatus, null, null, null, null, reviewCase.ReviewRound, "Employee not confirmed."), "Employee not confirmed.");
    }

    private async Task<Result<ConfirmationDecisionResultDto>> AdditionalReviewAsync(Guid tenantId, ConfirmationReviewCase reviewCase, EvaluationContext context, SubmitConfirmationDecisionRequest request, CancellationToken ct)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        await using var tx = await _db.BeginTransactionAsync(ct);

        // Additional review keeps the case active: Completed → InReview so another review cycle can run.
        // Additional Review creates a new review round/iteration - increment ReviewRound and preserve history.
        int newReviewRound = reviewCase.ReviewRound + 1;
        var rows = await _db.ConfirmationReviewCases
            .Where(c => c.Id == reviewCase.Id && c.Status == ConfirmationReviewCaseStatus.Completed && c.Outcome == ConfirmationOutcome.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, ConfirmationReviewCaseStatus.InReview)
                .SetProperty(c => c.ReviewRound, newReviewRound)
                .SetProperty(c => c.ModifiedDate, now), ct);
        if (rows != 1)
            return Result<ConfirmationDecisionResultDto>.Conflict("This case has already been decided.");

        var decision = AddDecision(tenantId, reviewCase, context, ConfirmationOutcome.AdditionalReview, request.DecisionReason,
            ConfirmationReviewCaseStatus.Completed, ConfirmationReviewCaseStatus.InReview, now, context.DeciderEmployeeId, reviewCase.ReviewRound);
        _db.ConfirmationReviewCaseEvents.Add(Event(tenantId, reviewCase.Id, ConfirmationReviewCaseEventType.DecisionSubmitted, now, "Decision submitted: Additional Review."));
        _db.ConfirmationReviewCaseEvents.Add(Event(tenantId, reviewCase.Id, ConfirmationReviewCaseEventType.AdditionalReviewRequested, now, Normalize(request.DecisionReason) ?? "Additional review requested."));

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Result<ConfirmationDecisionResultDto>.Success(new ConfirmationDecisionResultDto(
            decision.Id, reviewCase.Id, ConfirmationOutcome.AdditionalReview, ConfirmationReviewCaseStatus.Completed, ConfirmationReviewCaseStatus.InReview,
            context.Preview.OverallRating, context.Preview.AppliedRuleId, now, null, null, null, null, null, null, newReviewRound, "Additional review requested; the case remains open for a new review round."), "Additional review requested.");
    }

    private async Task<Result<ConfirmationDecisionResultDto>> ExtendAsync(Guid tenantId, ConfirmationReviewCase reviewCase, EvaluationContext context, SubmitConfirmationDecisionRequest request, CancellationToken ct)
    {
        var rule = context.PolicyRule;
        if (rule is null || rule.ExtensionDuration <= 0)
            return Result<ConfirmationDecisionResultDto>.Invalid("outcome", "The policy does not configure a probation extension duration.");

        var now = _time.GetUtcNow().UtcDateTime;
        Guid newCycleId;
        int newCycleNumber;
        DateOnly newEnd;
        Guid decisionId;

        await using (var tx = await _db.BeginTransactionAsync(ct))
        {
            if (!await CompareAndSwapToDecidedAsync(reviewCase.Id, ConfirmationOutcome.ProbationExtended, now, ct))
                return Result<ConfirmationDecisionResultDto>.Conflict("This case has already been decided.");

            // Find (or lazily create) the employee's active cycle, then close it and open a new one. Historical
            // cycles are preserved (the closed row stays), so the previous probation period is never overwritten.
            var active = await _db.ConfirmationProbationCycles
                .Where(c => c.EmployeeId == reviewCase.EmployeeId && c.Status == ConfirmationProbationCycleStatus.Active)
                .OrderByDescending(c => c.CycleNumber).FirstOrDefaultAsync(ct);

            if (active is null)
            {
                active = new ConfirmationProbationCycle
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = reviewCase.EmployeeId,
                    ConfirmationPolicyId = reviewCase.ConfirmationPolicyId, ConfirmationPolicyVersionId = reviewCase.ConfirmationPolicyVersionId,
                    CycleNumber = reviewCase.CycleNumber, StartDate = reviewCase.ProbationStartDate,
                    BaselineEndDate = reviewCase.ProbationEndDate, CurrentEndDate = reviewCase.ProbationEndDate,
                    Status = ConfirmationProbationCycleStatus.Active, ExtensionCount = 0, CreatedDate = now
                };
                _db.ConfirmationProbationCycles.Add(active);
            }

            if (active.ExtensionCount >= rule.MaxExtensions)
                return Result<ConfirmationDecisionResultDto>.Invalid("outcome", $"The maximum number of probation extensions ({rule.MaxExtensions}) has already been reached.");

            newEnd = ConfirmationDateCalculator.ExtendedProbationEndDate(active.CurrentEndDate, rule.ExtensionDuration, rule.ExtensionDurationUnit);
            newCycleNumber = active.CycleNumber + 1;
            var previousEnd = active.CurrentEndDate;

            active.Status = ConfirmationProbationCycleStatus.Closed;
            active.ModifiedDate = now;

            var newCycle = new ConfirmationProbationCycle
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = reviewCase.EmployeeId,
                ConfirmationPolicyId = reviewCase.ConfirmationPolicyId, ConfirmationPolicyVersionId = reviewCase.ConfirmationPolicyVersionId,
                CycleNumber = newCycleNumber, StartDate = active.StartDate,
                BaselineEndDate = newEnd, CurrentEndDate = newEnd,
                Status = ConfirmationProbationCycleStatus.Active, ExtensionCount = active.ExtensionCount + 1, CreatedDate = now
            };
            _db.ConfirmationProbationCycles.Add(newCycle);
            newCycleId = newCycle.Id;

            _db.ConfirmationProbationExtensions.Add(new ConfirmationProbationExtension
            {
                Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = reviewCase.EmployeeId,
                ConfirmationProbationCycleId = newCycle.Id, ConfirmationReviewCaseId = reviewCase.Id,
                PreviousEndDate = previousEnd, ExtensionDuration = rule.ExtensionDuration, ExtensionDurationUnit = rule.ExtensionDurationUnit,
                NewEndDate = newEnd, DecisionMakerUserId = _tenant.UserId, Reason = Normalize(request.DecisionReason), ExtendedAtUtc = now, CreatedDate = now
            });

            var decision = AddDecision(tenantId, reviewCase, context, ConfirmationOutcome.ProbationExtended, request.DecisionReason,
                ConfirmationReviewCaseStatus.Completed, ConfirmationReviewCaseStatus.Decided, now, context.DeciderEmployeeId, reviewCase.ReviewRound);
            decisionId = decision.Id;
            _db.ConfirmationReviewCaseEvents.Add(Event(tenantId, reviewCase.Id, ConfirmationReviewCaseEventType.DecisionSubmitted, now, $"Decision submitted: Extend Probation (rating {context.Preview.OverallRating?.ToString() ?? "n/a"})."));
            _db.ConfirmationReviewCaseEvents.Add(Event(tenantId, reviewCase.Id, ConfirmationReviewCaseEventType.ProbationExtended, now, $"Probation extended: {previousEnd} → {newEnd} (cycle {newCycleNumber})."));
            _db.ConfirmationReviewCaseEvents.Add(Event(tenantId, reviewCase.Id, ConfirmationReviewCaseEventType.ProbationCycleCreated, now, $"Probation cycle {newCycleNumber} created, end {newEnd}."));

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // After the new cycle is committed, the resolver returns the new probation-end, so a fresh Open review
        // case can be raised for the new cycle (idempotent; avoids duplicate cases). Best-effort: the extension
        // is already recorded even if case creation is deferred to the scheduler.
        Guid? newCaseId = null;
        var created = await _caseService.CreateCaseAsync(new CreateConfirmationReviewCaseRequest(reviewCase.EmployeeId), ct);
        if (created.Succeeded) newCaseId = created.Value!.Case.Id;

        return Result<ConfirmationDecisionResultDto>.Success(new ConfirmationDecisionResultDto(
            decisionId, reviewCase.Id, ConfirmationOutcome.ProbationExtended, ConfirmationReviewCaseStatus.Completed, ConfirmationReviewCaseStatus.Decided,
            context.Preview.OverallRating, context.Preview.AppliedRuleId, now, null, null, newCycleId, newCycleNumber, newEnd, newCaseId,
            reviewCase.ReviewRound, $"Probation extended to {newEnd} (cycle {newCycleNumber})."), "Probation extended.");
    }

    // ── evaluation ──────────────────────────────────────────────────────────

    private sealed record EvaluationContext(ConfirmationDecisionPreviewDto Preview, ConfirmationPolicyRule? PolicyRule, Guid? DeciderEmployeeId);

    private async Task<EvaluationContext> BuildEvaluationContextAsync(ConfirmationReviewCase reviewCase, CancellationToken ct)
    {
        var caseId = reviewCase.Id;
        var rule = await _db.ConfirmationPolicyRules.AsNoTracking().FirstOrDefaultAsync(x => x.ConfirmationPolicyVersionId == reviewCase.ConfirmationPolicyVersionId, ct);
        var requirement = rule?.RequiredApproval ?? ConfirmationApprovalRequirement.ManagerAndHrbp;
        var sequence = rule?.ApprovalSequence ?? ApprovalSequence.Parallel;
        var method = rule?.OverallRatingMethod ?? OverallRatingMethod.AverageOfRatings;
        var requireComments = rule?.RequireReviewerComments ?? false;

        // Current submitted reviews (superseded versions are excluded by status).
        var submitted = await _db.ConfirmationReviewerReviews.AsNoTracking()
            .Where(r => r.ConfirmationReviewCaseId == caseId && r.ReviewRound == reviewCase.ReviewRound && r.Status == ConfirmationReviewStatus.Submitted)
            .Select(r => new { r.Id, r.ReviewerType, r.OverallComments })
            .ToListAsync(ct);
        var submittedIds = submitted.Select(r => r.Id).ToList();
        var hasManager = submitted.Any(r => r.ReviewerType == ConfirmationReviewerType.ReportingManager);
        var hasHrbp = submitted.Any(r => r.ReviewerType == ConfirmationReviewerType.Hrbp);

        // Overall rating from the snapshot Rating questions across submitted reviews.
        var ratingQuestionIds = await _db.ConfirmationReviewCaseQuestions.AsNoTracking()
            .Where(q => q.ConfirmationReviewCaseId == caseId && q.QuestionType == ReviewQuestionType.Rating)
            .Select(q => q.Id).ToListAsync(ct);
        var ratingValues = submittedIds.Count == 0 || ratingQuestionIds.Count == 0
            ? new List<int>()
            : await _db.ConfirmationReviewResponses.AsNoTracking()
                .Where(r => submittedIds.Contains(r.ConfirmationReviewerReviewId) && r.RatingValue != null && ratingQuestionIds.Contains(r.ConfirmationReviewCaseQuestionId))
                .Select(r => r.RatingValue!.Value).ToListAsync(ct);

        var ratingCountByType = new Dictionary<ConfirmationReviewerType, int>();
        if (submittedIds.Count > 0 && ratingQuestionIds.Count > 0)
        {
            var byReview = await _db.ConfirmationReviewResponses.AsNoTracking()
                .Where(r => submittedIds.Contains(r.ConfirmationReviewerReviewId) && r.RatingValue != null && ratingQuestionIds.Contains(r.ConfirmationReviewCaseQuestionId))
                .GroupBy(r => r.ConfirmationReviewerReviewId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            foreach (var row in submitted)
                ratingCountByType[row.ReviewerType] = byReview.FirstOrDefault(b => b.Key == row.Id)?.Count ?? 0;
        }

        var overall = ConfirmationDecisionCalculator.CalculateOverallRating(ratingValues, method);

        var ruleCandidates = (await _db.ConfirmationDecisionRules.AsNoTracking()
            .Where(d => d.ConfirmationPolicyVersionId == reviewCase.ConfirmationPolicyVersionId)
            .ToListAsync(ct))
            .Select(d => new ConfirmationDecisionCalculator.RuleCandidate(d.Id, d.Priority, d.MinOverallRating, d.MaxOverallRating, d.Outcome, d.IsActive, d.Name))
            .ToList();
        var evaluation = ConfirmationDecisionCalculator.Evaluate(overall, method, ruleCandidates);

        // Reviewer status rows.
        var reviewers = new List<ConfirmationReviewerStatusDto>
        {
            BuildReviewerStatus(ConfirmationReviewerType.ReportingManager, requirement, hasManager, submitted.FirstOrDefault(r => r.ReviewerType == ConfirmationReviewerType.ReportingManager)?.OverallComments, ratingCountByType),
            BuildReviewerStatus(ConfirmationReviewerType.Hrbp, requirement, hasHrbp, submitted.FirstOrDefault(r => r.ReviewerType == ConfirmationReviewerType.Hrbp)?.OverallComments, ratingCountByType)
        };

        // Readiness / blocking reasons.
        var blocking = new List<string>();
        if (!ConfirmationReviewCaseService.IsApprovalComplete(requirement, hasManager, hasHrbp))
            blocking.Add("The required reviewer review(s) have not all been submitted.");
        if (requireComments)
        {
            foreach (var reviewer in reviewers.Where(r => r.IsRequired && r.IsSubmitted && !r.HasComments))
                blocking.Add($"The {reviewer.ReviewerType} review must include overall comments.");
        }

        var permitted = BuildPermittedOutcomes(ruleCandidates, rule, reviewCase);

        var preview = new ConfirmationDecisionPreviewDto(
            caseId, reviewCase.Status, reviewCase.Outcome, reviewCase.CycleNumber, reviewCase.ReviewRound, requirement, sequence, method,
            reviewers, blocking.Count == 0 && reviewCase.Status == ConfirmationReviewCaseStatus.Completed, blocking,
            overall, evaluation.AppliedRuleId, evaluation.AppliedRuleName, evaluation.Outcome, evaluation.Explanation, permitted);

        var deciderEmployeeId = await ResolveDeciderEmployeeIdAsync(ct);
        return new EvaluationContext(preview, rule, deciderEmployeeId);
    }

    private static ConfirmationReviewerStatusDto BuildReviewerStatus(ConfirmationReviewerType type, ConfirmationApprovalRequirement requirement, bool submitted, string? comments, IReadOnlyDictionary<ConfirmationReviewerType, int> ratingCounts)
    {
        var required = requirement switch
        {
            ConfirmationApprovalRequirement.Manager => type == ConfirmationReviewerType.ReportingManager,
            ConfirmationApprovalRequirement.Hrbp => type == ConfirmationReviewerType.Hrbp,
            ConfirmationApprovalRequirement.ManagerOrHrbp => false, // either suffices; neither is individually mandatory
            _ => true
        };
        return new ConfirmationReviewerStatusDto(type, required, submitted, !string.IsNullOrWhiteSpace(comments), ratingCounts.TryGetValue(type, out var c) ? c : 0);
    }

    private static IReadOnlyList<ConfirmationOutcome> BuildPermittedOutcomes(IReadOnlyList<ConfirmationDecisionCalculator.RuleCandidate> rules, ConfirmationPolicyRule? policyRule, ConfirmationReviewCase reviewCase)
    {
        var activeOutcomes = rules.Where(r => r.IsActive).Select(r => r.Outcome).ToHashSet();
        var permitted = new HashSet<ConfirmationOutcome>();
        if (activeOutcomes.Count > 0)
        {
            foreach (var o in activeOutcomes) permitted.Add(o);
        }
        else
        {
            // No decision rules configured: the authorized decider may apply any terminal outcome manually.
            permitted.Add(ConfirmationOutcome.Confirmed);
            permitted.Add(ConfirmationOutcome.NotConfirmed);
            permitted.Add(ConfirmationOutcome.ProbationExtended);
        }

        // Additional review is always a valid workflow control.
        permitted.Add(ConfirmationOutcome.AdditionalReview);

        // Extension is only permitted when the policy configures a duration; max-extension exhaustion is checked
        // at submit time against the live cycle.
        if (policyRule is null || policyRule.ExtensionDuration <= 0 || policyRule.MaxExtensions <= 0)
            permitted.Remove(ConfirmationOutcome.ProbationExtended);

        permitted.Remove(ConfirmationOutcome.Pending);
        return permitted.OrderBy(o => (int)o).ToList();
    }

    // ── persistence helpers ─────────────────────────────────────────────────

    private async Task<bool> CompareAndSwapToDecidedAsync(Guid caseId, ConfirmationOutcome outcome, DateTime now, CancellationToken ct)
    {
        // Atomic winner selection: only the transaction that flips Completed+Pending → Decided succeeds, so two
        // concurrent deciders cannot both confirm/extend/reject the same cycle.
        var rows = await _db.ConfirmationReviewCases
            .Where(c => c.Id == caseId && c.Status == ConfirmationReviewCaseStatus.Completed && c.Outcome == ConfirmationOutcome.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, ConfirmationReviewCaseStatus.Decided)
                .SetProperty(c => c.Outcome, outcome)
                .SetProperty(c => c.DecidedAtUtc, now)
                .SetProperty(c => c.DecisionMakerUserId, _tenant.UserId)
                .SetProperty(c => c.ModifiedDate, now), ct);
        return rows == 1;
    }

    private ConfirmationDecision AddDecision(Guid tenantId, ConfirmationReviewCase reviewCase, EvaluationContext context, ConfirmationOutcome outcome, string? reason,
        ConfirmationReviewCaseStatus previous, ConfirmationReviewCaseStatus next, DateTime now, Guid? deciderEmployeeId, int reviewRound)
    {
        var decision = new ConfirmationDecision
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewCaseId = reviewCase.Id,
            ProbationCycleKey = reviewCase.ProbationCycleKey, Outcome = outcome, DecisionReason = Normalize(reason),
            DecisionMakerUserId = _tenant.UserId, DecisionMakerEmployeeId = deciderEmployeeId, DecidedAtUtc = now,
            CalculatedOverallRating = context.Preview.OverallRating, AppliedRuleId = context.Preview.AppliedRuleId,
            PreviousCaseStatus = previous, NewCaseStatus = next, CreatedDate = now,
            ReviewRound = reviewRound
        };
        _db.ConfirmationDecisions.Add(decision);

        // A durable copy of the calculation that backed this decision.
        _db.ConfirmationDecisionCalculations.Add(new ConfirmationDecisionCalculation
        {
            Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewCaseId = reviewCase.Id,
            OverallRating = context.Preview.OverallRating, AppliedRuleId = context.Preview.AppliedRuleId,
            CalculatedOutcome = context.Preview.CalculatedOutcome, Explanation = Truncate(context.Preview.Explanation, 4000),
            ActorUserId = _tenant.UserId, CalculatedAtUtc = now, CreatedDate = now,
            ReviewRound = reviewRound
        });
        return decision;
    }

    private async Task<EmployeeEmployment> GetOrCreateEmploymentAsync(Guid tenantId, Guid employeeId, DateTime now, CancellationToken ct)
    {
        var employment = await _db.EmployeeEmployments.FirstOrDefaultAsync(e => e.EmployeeId == employeeId, ct);
        if (employment is null)
        {
            employment = new EmployeeEmployment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, CreatedDate = now };
            _db.EmployeeEmployments.Add(employment);
        }
        return employment;
    }

    private async Task CloseActiveCycleAsync(Guid employeeId, DateTime now, CancellationToken ct)
    {
        await _db.ConfirmationProbationCycles
            .Where(c => c.EmployeeId == employeeId && c.Status == ConfirmationProbationCycleStatus.Active)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, ConfirmationProbationCycleStatus.Closed)
                .SetProperty(c => c.ModifiedDate, now), ct);
    }

    private async Task<Guid?> ResolveDeciderEmployeeIdAsync(CancellationToken ct)
    {
        if (_tenant.UserId is not Guid userId) return null;
        return await _db.AccountEmployeeCurrentLinks.AsNoTracking().Where(l => l.UserId == userId).Select(l => (Guid?)l.EmployeeId).FirstOrDefaultAsync(ct);
    }

    private ConfirmationReviewCaseEvent Event(Guid tenantId, Guid caseId, ConfirmationReviewCaseEventType type, DateTime now, string details) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewCaseId = caseId, EventType = type,
        ActorUserId = _tenant.UserId, Details = Truncate(details, 4000), OccurredAtUtc = now, CreatedDate = now
    };

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
