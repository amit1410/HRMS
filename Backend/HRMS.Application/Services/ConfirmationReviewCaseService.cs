using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Application.Services;

/// <summary>
/// Phase 9c-2 review case lifecycle. Creation resolves the applicable policy (9a), snapshots the applicable
/// 9c-1 questions onto the case so later master edits never change history, and resolves the Reporting
/// Manager (via <see cref="IEmployeeManagerResolver"/>) and HR/HRBP (via <see cref="IConfirmationHrbpResolver"/>)
/// reviewers. Reviewers save drafts and submit; a submitted review is immutable — a correction creates a new
/// version and supersedes the prior one. Every mutation appends a <see cref="ConfirmationReviewCaseEvent"/>.
/// This service collects reviews only; it makes no decision and changes no employee status.
/// </summary>
public sealed class ConfirmationReviewCaseService : IConfirmationReviewCaseService
{
    private const string NoTenant = "No authenticated tenant.";
    private const string CaseNotFound = "Review case not found.";
    private const string NotAuthorized = "You are not authorized to act on this review case.";

    private readonly IHrmsDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IConfirmationPolicyResolver _policyResolver;
    private readonly IConfirmationReviewQuestionResolver _questionResolver;
    private readonly IConfirmationHrbpResolver _hrbpResolver;
    private readonly IEmployeeManagerResolver _managerResolver;
    private readonly IEmployeeAccessScopeService _accessScope;
    private readonly TimeProvider _time;

    public ConfirmationReviewCaseService(
        IHrmsDbContext db,
        ITenantContext tenant,
        IConfirmationPolicyResolver policyResolver,
        IConfirmationReviewQuestionResolver questionResolver,
        IConfirmationHrbpResolver hrbpResolver,
        IEmployeeManagerResolver managerResolver,
        IEmployeeAccessScopeService accessScope,
        TimeProvider? time = null)
    {
        _db = db;
        _tenant = tenant;
        _policyResolver = policyResolver;
        _questionResolver = questionResolver;
        _hrbpResolver = hrbpResolver;
        _managerResolver = managerResolver;
        _accessScope = accessScope;
        _time = time ?? TimeProvider.System;
    }

    private DateOnly Today => DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime);

    public async Task<Result<PagedResult<ConfirmationReviewCaseDto>>> GetCasesAsync(ConfirmationReviewCaseQuery query, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<PagedResult<ConfirmationReviewCaseDto>>.Unauthorized(NoTenant);

        var predicate = await _accessScope.BuildPredicateAsync(Today, ct);
        var accessibleIds = _db.Employees.Where(predicate).Select(e => e.Id);

        var source = _db.ConfirmationReviewCases.AsNoTracking().Where(c => accessibleIds.Contains(c.EmployeeId));
        if (query.EmployeeId is Guid eid) source = source.Where(c => c.EmployeeId == eid);
        if (query.Status is ConfirmationReviewCaseStatus st) source = source.Where(c => c.Status == st);

        var ordered = ApplySort(source, query);
        var page = await ordered.Select(c => new ConfirmationReviewCaseDto(
                c.Id, c.EmployeeId,
                _db.Employees.Where(e => e.Id == c.EmployeeId).Select(e => e.EmployeeCode).FirstOrDefault(),
                _db.Employees.Where(e => e.Id == c.EmployeeId).Select(e => (e.FirstName + " " + e.LastName)).FirstOrDefault(),
                c.ConfirmationPolicyId, c.ConfirmationPolicyVersionId, c.ProbationCycleKey, c.ReviewRound,
                c.ProbationStartDate, c.ProbationEndDate, c.DueDate, c.Status,
                c.ManagerReviewerEmployeeId, c.HrbpReviewerEmployeeId,
                c.Questions.Count, c.CreatedDate, c.ModifiedDate))
            .ToPagedResultAsync(query, ct);
        return Result<PagedResult<ConfirmationReviewCaseDto>>.Success(page);
    }

    public async Task<Result<ConfirmationReviewCaseDetailDto>> GetCaseAsync(Guid caseId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<ConfirmationReviewCaseDetailDto>.Unauthorized(NoTenant);
        var reviewCase = await _db.ConfirmationReviewCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caseId, ct);
        if (reviewCase is null) return Result<ConfirmationReviewCaseDetailDto>.NotFound(CaseNotFound);
        if (!await _accessScope.CanAccessEmployeeAsync(reviewCase.EmployeeId, Today, ct))
            return Result<ConfirmationReviewCaseDetailDto>.Forbidden(NotAuthorized);
        return Result<ConfirmationReviewCaseDetailDto>.Success(await BuildDetailAsync(reviewCase, ct));
    }

    public async Task<Result<ConfirmationReviewCaseDetailDto>> CreateCaseAsync(CreateConfirmationReviewCaseRequest request, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) return Result<ConfirmationReviewCaseDetailDto>.Unauthorized(NoTenant);
        var effectiveDate = request.EffectiveDate ?? Today;

        var employee = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct);
        if (employee is null) return Result<ConfirmationReviewCaseDetailDto>.Invalid("employeeId", "Employee was not found in the tenant.");
        if (!await _accessScope.CanAccessEmployeeAsync(request.EmployeeId, Today, ct))
            return Result<ConfirmationReviewCaseDetailDto>.Forbidden(NotAuthorized);

        var policy = await _policyResolver.ResolveAsync(tenantId, request.EmployeeId, effectiveDate, ct);
        if (policy.Status != ConfirmationPolicyResolutionStatus.Resolved)
        {
            return policy.Status switch
            {
                ConfirmationPolicyResolutionStatus.NoPolicy => Result<ConfirmationReviewCaseDetailDto>.Invalid("employeeId", "No published confirmation policy applies to this employee."),
                ConfirmationPolicyResolutionStatus.ConfigurationAmbiguity => Result<ConfirmationReviewCaseDetailDto>.Conflict(policy.Message),
                _ => Result<ConfirmationReviewCaseDetailDto>.Invalid("employeeId", policy.Message)
            };
        }

        var probationStart = policy.DateOfJoining!.Value;
        var probationEnd = policy.ProbationEndDate!.Value;
        var dueDate = probationEnd.AddDays(-(policy.TriggerDaysBeforeEnd ?? 0));
        var cycleKey = $"{policy.ConfirmationPolicyVersionId:N}:{probationStart:yyyy-MM-dd}:{probationEnd:yyyy-MM-dd}";

        // Idempotent: a case for this employee+cycle already exists → return it unchanged.
        var existing = await _db.ConfirmationReviewCases.AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmployeeId == request.EmployeeId && c.ProbationCycleKey == cycleKey, ct);
        if (existing is not null)
            return Result<ConfirmationReviewCaseDetailDto>.Success(await BuildDetailAsync(existing, ct), "Existing review case returned.");

        var questions = await _questionResolver.ResolveAsync(tenantId, request.EmployeeId, effectiveDate, policy.ConfirmationPolicyId, ct);
        if (questions.Status != ConfirmationReviewQuestionResolutionStatus.Resolved)
            return Result<ConfirmationReviewCaseDetailDto>.Invalid("employeeId", questions.Message);

        var manager = await _managerResolver.ResolveAsync(request.EmployeeId, effectiveDate, ct);
        Guid? managerEmployeeId = manager.Succeeded && manager.Value!.Status == EmployeeManagerResolutionStatus.Resolved ? manager.Value.ManagerId : null;

        var hrbp = await _hrbpResolver.ResolveAsync(tenantId, request.EmployeeId, effectiveDate, ct);
        Guid? hrbpEmployeeId = hrbp.Status == ConfirmationHrbpResolutionStatus.Resolved ? hrbp.HrbpEmployeeId : null;

        // Phase 9c-3: stamp the case with the employee's current probation cycle number (1 unless an extension
        // has created a later active cycle) so decision history and cycle identity line up across extensions.
        var cycleNumber = await _db.ConfirmationProbationCycles.AsNoTracking()
            .Where(x => x.EmployeeId == request.EmployeeId && x.Status == ConfirmationProbationCycleStatus.Active)
            .OrderByDescending(x => x.CycleNumber).Select(x => (int?)x.CycleNumber).FirstOrDefaultAsync(ct) ?? 1;

        var now = _time.GetUtcNow().UtcDateTime;
        var caseId = Guid.NewGuid();
        var reviewCase = new ConfirmationReviewCase
        {
            Id = caseId, TenantId = tenantId, EmployeeId = request.EmployeeId,
            ConfirmationPolicyId = policy.ConfirmationPolicyId!.Value, ConfirmationPolicyVersionId = policy.ConfirmationPolicyVersionId!.Value,
            ProbationCycleKey = cycleKey, ProbationStartDate = probationStart, ProbationEndDate = probationEnd, DueDate = dueDate,
            Status = ConfirmationReviewCaseStatus.Open, CycleNumber = cycleNumber,
            ManagerReviewerEmployeeId = managerEmployeeId, HrbpReviewerEmployeeId = hrbpEmployeeId,
            CreatedBy = _tenant.UserId?.ToString()
        };

        foreach (var q in questions.Questions)
        {
            var snapshotQuestion = new ConfirmationReviewCaseQuestion
            {
                Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewCaseId = caseId, SourceQuestionId = q.QuestionId,
                Code = q.Code, QuestionText = q.QuestionText, HelpText = q.HelpText, QuestionType = q.QuestionType,
                IsMandatory = q.IsMandatory, DisplayOrder = q.DisplayOrder,
                SourceRatingScaleId = q.RatingScaleId, RatingScaleCode = q.RatingScaleCode, RatingScaleName = q.RatingScaleName,
                RatingMin = q.RatingMin, RatingMax = q.RatingMax
            };
            foreach (var o in q.Options)
                snapshotQuestion.Options.Add(new ConfirmationReviewCaseQuestionOption
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewCaseQuestionId = snapshotQuestion.Id,
                    Code = o.Code, Label = o.Label, DisplayOrder = o.DisplayOrder
                });
            foreach (var v in q.RatingValues)
                snapshotQuestion.RatingValues.Add(new ConfirmationReviewCaseRatingValue
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewCaseQuestionId = snapshotQuestion.Id,
                    RatingValue = v.RatingValue, Label = v.Label, DisplayOrder = v.DisplayOrder
                });
            reviewCase.Questions.Add(snapshotQuestion);
        }

        reviewCase.Events.Add(Event(tenantId, caseId, ConfirmationReviewCaseEventType.CaseCreated, now, null, $"Case created for cycle {cycleKey} (policy version {policy.ConfirmationPolicyVersionId})."));
        reviewCase.Events.Add(Event(tenantId, caseId, ConfirmationReviewCaseEventType.QuestionsSnapshotted, now, null, $"Snapshotted {questions.Questions.Count} question(s)."));
        reviewCase.Events.Add(Event(tenantId, caseId, ConfirmationReviewCaseEventType.ReviewersResolved, now, null,
            $"Manager={(managerEmployeeId?.ToString() ?? "unassigned")}; HRBP={(hrbpEmployeeId?.ToString() ?? "unassigned")} ({hrbp.Status})."));

        _db.ConfirmationReviewCases.Add(reviewCase);
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Lost the race — another call created the same cycle's case. Return the winner.
            var winner = await _db.ConfirmationReviewCases.AsNoTracking()
                .FirstOrDefaultAsync(c => c.EmployeeId == request.EmployeeId && c.ProbationCycleKey == cycleKey, ct);
            if (winner is not null) return Result<ConfirmationReviewCaseDetailDto>.Success(await BuildDetailAsync(winner, ct), "Existing review case returned.");
            throw;
        }

        return Result<ConfirmationReviewCaseDetailDto>.Success(await BuildDetailAsync(reviewCase, ct), "Review case created.");
    }

    public async Task<Result<ConfirmationReviewerFormDto>> GetReviewerFormAsync(Guid caseId, ConfirmationReviewerType reviewerType, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<ConfirmationReviewerFormDto>.Unauthorized(NoTenant);
        var reviewCase = await _db.ConfirmationReviewCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caseId, ct);
        if (reviewCase is null) return Result<ConfirmationReviewerFormDto>.NotFound(CaseNotFound);
        if (!await _accessScope.CanAccessEmployeeAsync(reviewCase.EmployeeId, Today, ct))
            return Result<ConfirmationReviewerFormDto>.Forbidden(NotAuthorized);

        var caseDto = await BuildCaseDtoAsync(reviewCase, ct);
        var questions = await BuildQuestionsAsync(caseId, ct);
        var current = await CurrentReviewAsync(caseId, reviewCase.ReviewRound, reviewerType, ct);
        var currentDto = current is null ? null : await BuildReviewDtoAsync(current, ct);
        return Result<ConfirmationReviewerFormDto>.Success(new ConfirmationReviewerFormDto(caseDto, reviewerType, questions, currentDto));
    }

    public async Task<Result<ConfirmationReviewerReviewDto>> SaveDraftAsync(Guid caseId, ConfirmationReviewerType reviewerType, SaveReviewerDraftRequest request, CancellationToken ct = default)
    {
        var ctx = await LoadForWriteAsync<ConfirmationReviewerReviewDto>(caseId, ct);
        if (ctx.Error is not null) return ctx.Error;
        var reviewCase = ctx.Case!;
        if (!await CanActAsReviewerAsync(reviewCase, reviewerType, ct))
            return Result<ConfirmationReviewerReviewDto>.Forbidden("Only the assigned reviewer may edit this review.");

        var snapshot = await _db.ConfirmationReviewCaseQuestions.AsNoTracking().Where(q => q.ConfirmationReviewCaseId == caseId).ToListAsync(ct);
        var validation = ValidateResponses(snapshot, await LoadSnapshotOptionsAsync(caseId, ct), await LoadSnapshotRatingsAsync(caseId, ct), request.Responses, requireMandatory: false);
        if (validation is not null) return validation;

        var review = await GetOrCreateDraftAsync(reviewCase, reviewerType, ct);
        review.OverallComments = Normalize(request.OverallComments);
        await ReplaceResponsesAsync(review, request.Responses, ct);

        var now = _time.GetUtcNow().UtcDateTime;
        review.ModifiedDate = now;
        _db.ConfirmationReviewCaseEvents.Add(Event(reviewCase.TenantId, caseId, ConfirmationReviewCaseEventType.DraftSaved, now, review.Id, $"{reviewerType} draft v{review.VersionNumber} saved."));
        await _db.SaveChangesAsync(ct);

        return Result<ConfirmationReviewerReviewDto>.Success(await BuildReviewDtoAsync(review, ct), "Draft saved.");
    }

    public async Task<Result<ConfirmationReviewerReviewDto>> SubmitReviewAsync(Guid caseId, ConfirmationReviewerType reviewerType, SubmitReviewerReviewRequest request, CancellationToken ct = default)
    {
        var ctx = await LoadForWriteAsync<ConfirmationReviewerReviewDto>(caseId, ct);
        if (ctx.Error is not null) return ctx.Error;
        var reviewCase = ctx.Case!;
        if (!await CanActAsReviewerAsync(reviewCase, reviewerType, ct))
            return Result<ConfirmationReviewerReviewDto>.Forbidden("Only the assigned reviewer may submit this review.");

        // Approval order: when the policy requires Manager-then-HRBP, the HRBP review cannot be submitted until the
        // Manager review has been submitted.
        var approval = await LoadApprovalConfigAsync(reviewCase, ct);
        if (approval.Sequence == ApprovalSequence.ManagerThenHrbp && reviewerType == ConfirmationReviewerType.Hrbp)
        {
            var managerSubmitted = await _db.ConfirmationReviewerReviews.AsNoTracking()
                .AnyAsync(r => r.ConfirmationReviewCaseId == caseId && r.ReviewRound == reviewCase.ReviewRound && r.ReviewerType == ConfirmationReviewerType.ReportingManager && r.Status == ConfirmationReviewStatus.Submitted, ct);
            if (!managerSubmitted)
                return Result<ConfirmationReviewerReviewDto>.Conflict("The Reporting Manager review must be submitted before the HR/HRBP review (policy requires Manager-then-HRBP ordering).");
        }

        var snapshot = await _db.ConfirmationReviewCaseQuestions.AsNoTracking().Where(q => q.ConfirmationReviewCaseId == caseId).ToListAsync(ct);
        var options = await LoadSnapshotOptionsAsync(caseId, ct);
        var ratings = await LoadSnapshotRatingsAsync(caseId, ct);
        var validation = ValidateResponses(snapshot, options, ratings, request.Responses, requireMandatory: true);
        if (validation is not null) return validation;

        var now = _time.GetUtcNow().UtcDateTime;
        var reviewerEmployeeId = await ResolveReviewerEmployeeIdAsync(reviewCase, reviewerType, ct);

        // A submitted review is immutable: supersede it and create a new version.
        var current = await CurrentReviewAsync(caseId, reviewCase.ReviewRound, reviewerType, ct);
        int nextVersion = 1;
        if (current is not null && current.Status == ConfirmationReviewStatus.Submitted)
        {
            var tracked = await _db.ConfirmationReviewerReviews.FirstAsync(r => r.Id == current.Id, ct);
            tracked.Status = ConfirmationReviewStatus.Superseded;
            tracked.ModifiedDate = now;
            nextVersion = tracked.VersionNumber + 1;
            _db.ConfirmationReviewCaseEvents.Add(Event(reviewCase.TenantId, caseId, ConfirmationReviewCaseEventType.ReviewSuperseded, now, tracked.Id, $"{reviewerType} review v{tracked.VersionNumber} superseded."));
        }

        ConfirmationReviewerReview review;
        if (current is not null && current.Status == ConfirmationReviewStatus.Draft)
        {
            review = await _db.ConfirmationReviewerReviews.FirstAsync(r => r.Id == current.Id, ct);
        }
        else
        {
            review = NewReview(reviewCase.TenantId, caseId, reviewCase.ReviewRound, reviewerType, nextVersion);
            _db.ConfirmationReviewerReviews.Add(review);
        }

        review.OverallComments = Normalize(request.OverallComments);
        review.Status = ConfirmationReviewStatus.Submitted;
        review.SubmittedAtUtc = now;
        review.ReviewerEmployeeId = reviewerEmployeeId;
        review.SubmittedBy = _tenant.UserId?.ToString();
        review.ModifiedDate = now;
        await ReplaceResponsesAsync(review, request.Responses, ct);

        _db.ConfirmationReviewCaseEvents.Add(Event(reviewCase.TenantId, caseId, ConfirmationReviewCaseEventType.ReviewSubmitted, now, review.Id, $"{reviewerType} review v{review.VersionNumber} submitted."));

        await UpdateCaseStatusAfterSubmitAsync(reviewCase, reviewerType, now, ct);
        await _db.SaveChangesAsync(ct);

        return Result<ConfirmationReviewerReviewDto>.Success(await BuildReviewDtoAsync(review, ct), "Review submitted.");
    }

    public async Task<Result<IReadOnlyList<ConfirmationReviewCaseEventDto>>> GetHistoryAsync(Guid caseId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is null) return Result<IReadOnlyList<ConfirmationReviewCaseEventDto>>.Unauthorized(NoTenant);
        var reviewCase = await _db.ConfirmationReviewCases.AsNoTracking().FirstOrDefaultAsync(c => c.Id == caseId, ct);
        if (reviewCase is null) return Result<IReadOnlyList<ConfirmationReviewCaseEventDto>>.NotFound(CaseNotFound);
        if (!await _accessScope.CanAccessEmployeeAsync(reviewCase.EmployeeId, Today, ct))
            return Result<IReadOnlyList<ConfirmationReviewCaseEventDto>>.Forbidden(NotAuthorized);

        var events = await _db.ConfirmationReviewCaseEvents.AsNoTracking()
            .Where(e => e.ConfirmationReviewCaseId == caseId)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .Select(e => new ConfirmationReviewCaseEventDto(e.Id, e.EventType, e.ConfirmationReviewerReviewId, e.ActorUserId, e.Details, e.OccurredAtUtc))
            .ToListAsync(ct);
        return Result<IReadOnlyList<ConfirmationReviewCaseEventDto>>.Success(events);
    }

    // --- write helpers ---

    private sealed record WriteContext<T>(ConfirmationReviewCase? Case, Result<T>? Error);

    private async Task<WriteContext<T>> LoadForWriteAsync<T>(Guid caseId, CancellationToken ct)
    {
        if (_tenant.TenantId is null) return new(null, Result<T>.Unauthorized(NoTenant));
        var reviewCase = await _db.ConfirmationReviewCases.FirstOrDefaultAsync(c => c.Id == caseId, ct);
        if (reviewCase is null) return new(null, Result<T>.NotFound(CaseNotFound));
        if (!await _accessScope.CanAccessEmployeeAsync(reviewCase.EmployeeId, Today, ct))
            return new(null, Result<T>.Forbidden(NotAuthorized));
        // A decided case is terminal; its reviews are frozen. Another review cycle requires a new case.
        if (reviewCase.Status == ConfirmationReviewCaseStatus.Decided)
            return new(null, Result<T>.Conflict("This case has already been decided; its reviews can no longer be changed."));
        return new(reviewCase, null);
    }

    /// <summary>The approval requirement + sequence configured on the case's (frozen) policy version.</summary>
    private async Task<(ConfirmationApprovalRequirement Requirement, ApprovalSequence Sequence)> LoadApprovalConfigAsync(ConfirmationReviewCase reviewCase, CancellationToken ct)
    {
        var rule = await _db.ConfirmationPolicyRules.AsNoTracking()
            .Where(x => x.ConfirmationPolicyVersionId == reviewCase.ConfirmationPolicyVersionId)
            .Select(x => new { x.RequiredApproval, x.ApprovalSequence })
            .FirstOrDefaultAsync(ct);
        return (rule?.RequiredApproval ?? ConfirmationApprovalRequirement.ManagerAndHrbp, rule?.ApprovalSequence ?? ApprovalSequence.Parallel);
    }

    /// <summary>Whether, given the configured requirement, the set of submitted reviewer types is complete.</summary>
    internal static bool IsApprovalComplete(ConfirmationApprovalRequirement requirement, bool hasManager, bool hasHrbp) => requirement switch
    {
        ConfirmationApprovalRequirement.Manager => hasManager,
        ConfirmationApprovalRequirement.Hrbp => hasHrbp,
        ConfirmationApprovalRequirement.ManagerOrHrbp => hasManager || hasHrbp,
        _ => hasManager && hasHrbp
    };

    private async Task<ConfirmationReviewerReview> GetOrCreateDraftAsync(ConfirmationReviewCase reviewCase, ConfirmationReviewerType reviewerType, CancellationToken ct)
    {
        var current = await CurrentReviewAsync(reviewCase.Id, reviewCase.ReviewRound, reviewerType, ct);
        if (current is not null && current.Status == ConfirmationReviewStatus.Draft)
            return await _db.ConfirmationReviewerReviews.FirstAsync(r => r.Id == current.Id, ct);

        var nextVersion = current is null ? 1 : current.VersionNumber + 1;
        var review = NewReview(reviewCase.TenantId, reviewCase.Id, reviewCase.ReviewRound, reviewerType, nextVersion);
        _db.ConfirmationReviewerReviews.Add(review);
        return review;
    }

    private async Task ReplaceResponsesAsync(ConfirmationReviewerReview review, IReadOnlyList<ConfirmationReviewResponseInput>? inputs, CancellationToken ct)
    {
        var existing = await _db.ConfirmationReviewResponses.Where(r => r.ConfirmationReviewerReviewId == review.Id).Select(r => r.Id).ToListAsync(ct);
        if (existing.Count > 0)
        {
            var existingOptions = await _db.ConfirmationReviewResponseOptions.Where(o => existing.Contains(o.ConfirmationReviewResponseId)).ToListAsync(ct);
            _db.ConfirmationReviewResponseOptions.RemoveRange(existingOptions);
            var existingResponses = await _db.ConfirmationReviewResponses.Where(r => r.ConfirmationReviewerReviewId == review.Id).ToListAsync(ct);
            _db.ConfirmationReviewResponses.RemoveRange(existingResponses);
        }
        if (inputs is null) return;

        foreach (var input in inputs)
        {
            var response = new ConfirmationReviewResponse
            {
                Id = Guid.NewGuid(), TenantId = review.TenantId, ConfirmationReviewerReviewId = review.Id,
                ConfirmationReviewCaseQuestionId = input.ConfirmationReviewCaseQuestionId,
                RatingValue = input.RatingValue, BoolValue = input.BoolValue, TextValue = Normalize(input.TextValue)
            };
            _db.ConfirmationReviewResponses.Add(response);
            foreach (var code in (input.SelectedOptionCodes ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase))
                _db.ConfirmationReviewResponseOptions.Add(new ConfirmationReviewResponseOption
                {
                    Id = Guid.NewGuid(), TenantId = review.TenantId, ConfirmationReviewResponseId = response.Id, OptionCode = code
                });
        }
    }

    private async Task<Guid?> ResolveReviewerEmployeeIdAsync(ConfirmationReviewCase reviewCase, ConfirmationReviewerType reviewerType, CancellationToken ct)
    {
        var assigned = reviewerType == ConfirmationReviewerType.ReportingManager ? reviewCase.ManagerReviewerEmployeeId : reviewCase.HrbpReviewerEmployeeId;
        if (assigned is Guid id) return id;
        if (_tenant.UserId is not Guid userId) return null;
        return await _db.AccountEmployeeCurrentLinks.AsNoTracking().Where(l => l.UserId == userId).Select(l => (Guid?)l.EmployeeId).FirstOrDefaultAsync(ct);
    }

    private async Task<bool> CanActAsReviewerAsync(ConfirmationReviewCase reviewCase, ConfirmationReviewerType reviewerType, CancellationToken ct)
    {
        var assigned = reviewerType == ConfirmationReviewerType.ReportingManager
            ? reviewCase.ManagerReviewerEmployeeId
            : reviewCase.HrbpReviewerEmployeeId;
        if (assigned is null) return true;
        if (_tenant.UserId is not Guid userId) return false;
        return await _db.AccountEmployeeCurrentLinks.AsNoTracking()
            .AnyAsync(link => link.UserId == userId && link.EmployeeId == assigned.Value, ct);
    }

    private async Task UpdateCaseStatusAfterSubmitAsync(ConfirmationReviewCase reviewCase, ConfirmationReviewerType justSubmitted, DateTime now, CancellationToken ct)
    {
        var submittedTypes = await _db.ConfirmationReviewerReviews.AsNoTracking()
            .Where(r => r.ConfirmationReviewCaseId == reviewCase.Id && r.ReviewRound == reviewCase.ReviewRound && r.Status == ConfirmationReviewStatus.Submitted)
            .Select(r => r.ReviewerType).Distinct().ToListAsync(ct);
        // The review being submitted is tracked but not yet persisted, so a query won't return it — include it explicitly.
        var hasManager = justSubmitted == ConfirmationReviewerType.ReportingManager || submittedTypes.Contains(ConfirmationReviewerType.ReportingManager);
        var hasHrbp = justSubmitted == ConfirmationReviewerType.Hrbp || submittedTypes.Contains(ConfirmationReviewerType.Hrbp);
        // "Completed" now means the *required* reviews (per the policy's approval requirement) are in — not always
        // both — so a Manager-only or HRBP-only policy becomes decidable with a single review.
        var approval = await LoadApprovalConfigAsync(reviewCase, ct);
        var complete = IsApprovalComplete(approval.Requirement, hasManager, hasHrbp);

        // For Additional Review outcome, we keep the case active (InReview) regardless of approval completion.
        // Additional Review creates a new review round/iteration - the decision service will handle
        // incrementing the review round when applying Additional Review outcome.
        var newStatus = complete ? ConfirmationReviewCaseStatus.Completed : ConfirmationReviewCaseStatus.InReview;
        if (reviewCase.Status != newStatus)
        {
            reviewCase.Status = newStatus;
            reviewCase.ModifiedDate = now;
        }
    }

    // --- validation ---

    private static Result<ConfirmationReviewerReviewDto>? ValidateResponses(
        IReadOnlyList<ConfirmationReviewCaseQuestion> questions,
        ILookup<Guid, string> optionsByQuestion,
        ILookup<Guid, int> ratingsByQuestion,
        IReadOnlyList<ConfirmationReviewResponseInput>? inputs,
        bool requireMandatory)
    {
        var errors = new List<ValidationError>();
        var byQuestion = (inputs ?? Array.Empty<ConfirmationReviewResponseInput>())
            .GroupBy(i => i.ConfirmationReviewCaseQuestionId).ToDictionary(g => g.Key, g => g.Last());
        foreach (var duplicate in (inputs ?? Array.Empty<ConfirmationReviewResponseInput>()).GroupBy(i => i.ConfirmationReviewCaseQuestionId).Where(g => g.Count() > 1))
            errors.Add(new("responses", $"Question {duplicate.Key} has more than one response."));
        var questionIds = questions.Select(q => q.Id).ToHashSet();

        foreach (var input in byQuestion.Values)
            if (!questionIds.Contains(input.ConfirmationReviewCaseQuestionId))
                errors.Add(new("responses", $"Response references a question ({input.ConfirmationReviewCaseQuestionId}) that is not part of this case."));

        foreach (var q in questions)
        {
            byQuestion.TryGetValue(q.Id, out var input);
            var answered = IsAnswered(q.QuestionType, input);
            if (!answered)
            {
                if (requireMandatory && q.IsMandatory) errors.Add(new($"responses.{q.Code}", $"'{q.QuestionText}' is required."));
                continue;
            }

            switch (q.QuestionType)
            {
                case ReviewQuestionType.Rating:
                    if (input!.RatingValue is int rv && !ratingsByQuestion[q.Id].Contains(rv))
                        errors.Add(new($"responses.{q.Code}", "Rating is not a value on this question's rating scale."));
                    break;
                case ReviewQuestionType.Dropdown:
                    var selected = input!.SelectedOptionCodes ?? Array.Empty<string>();
                    if (selected.Count > 1) errors.Add(new($"responses.{q.Code}", "A dropdown question accepts a single option."));
                    foreach (var code in selected)
                        if (!optionsByQuestion[q.Id].Contains(code, StringComparer.OrdinalIgnoreCase))
                            errors.Add(new($"responses.{q.Code}", $"'{code}' is not a configured option."));
                    break;
                case ReviewQuestionType.MultiSelect:
                    foreach (var code in input!.SelectedOptionCodes ?? Array.Empty<string>())
                        if (!optionsByQuestion[q.Id].Contains(code, StringComparer.OrdinalIgnoreCase))
                            errors.Add(new($"responses.{q.Code}", $"'{code}' is not a configured option."));
                    break;
                case ReviewQuestionType.Text:
                    if ((input!.TextValue?.Length ?? 0) > 4000) errors.Add(new($"responses.{q.Code}", "Text answer exceeds 4000 characters."));
                    break;
            }
        }

        return errors.Count == 0 ? null : Result<ConfirmationReviewerReviewDto>.Invalid("One or more responses are invalid.", errors);
    }

    private static bool IsAnswered(ReviewQuestionType type, ConfirmationReviewResponseInput? input) => type switch
    {
        _ when input is null => false,
        ReviewQuestionType.Rating => input.RatingValue.HasValue,
        ReviewQuestionType.YesNo => input.BoolValue.HasValue,
        ReviewQuestionType.Text => !string.IsNullOrWhiteSpace(input.TextValue),
        ReviewQuestionType.Dropdown or ReviewQuestionType.MultiSelect => (input.SelectedOptionCodes?.Count ?? 0) > 0,
        _ => false
    };

    private async Task<ILookup<Guid, string>> LoadSnapshotOptionsAsync(Guid caseId, CancellationToken ct)
    {
        var rows = await _db.ConfirmationReviewCaseQuestionOptions.AsNoTracking()
            .Where(o => _db.ConfirmationReviewCaseQuestions.Any(q => q.Id == o.ConfirmationReviewCaseQuestionId && q.ConfirmationReviewCaseId == caseId))
            .Select(o => new { o.ConfirmationReviewCaseQuestionId, o.Code }).ToListAsync(ct);
        return rows.ToLookup(x => x.ConfirmationReviewCaseQuestionId, x => x.Code);
    }

    private async Task<ILookup<Guid, int>> LoadSnapshotRatingsAsync(Guid caseId, CancellationToken ct)
    {
        var rows = await _db.ConfirmationReviewCaseRatingValues.AsNoTracking()
            .Where(v => _db.ConfirmationReviewCaseQuestions.Any(q => q.Id == v.ConfirmationReviewCaseQuestionId && q.ConfirmationReviewCaseId == caseId))
            .Select(v => new { v.ConfirmationReviewCaseQuestionId, v.RatingValue }).ToListAsync(ct);
        return rows.ToLookup(x => x.ConfirmationReviewCaseQuestionId, x => x.RatingValue);
    }

    // --- read/projection helpers ---

    private async Task<ConfirmationReviewerReview?> CurrentReviewAsync(Guid caseId, int reviewRound, ConfirmationReviewerType reviewerType, CancellationToken ct) =>
        await _db.ConfirmationReviewerReviews.AsNoTracking()
            .Where(r => r.ConfirmationReviewCaseId == caseId && r.ReviewRound == reviewRound && r.ReviewerType == reviewerType && r.Status != ConfirmationReviewStatus.Superseded)
            .OrderByDescending(r => r.VersionNumber).FirstOrDefaultAsync(ct);

    private async Task<ConfirmationReviewCaseDetailDto> BuildDetailAsync(ConfirmationReviewCase reviewCase, CancellationToken ct)
    {
        var caseDto = await BuildCaseDtoAsync(reviewCase, ct);
        var questions = await BuildQuestionsAsync(reviewCase.Id, ct);
        var reviews = await _db.ConfirmationReviewerReviews.AsNoTracking()
            .Where(r => r.ConfirmationReviewCaseId == reviewCase.Id)
            .OrderBy(r => r.ReviewerType).ThenBy(r => r.VersionNumber).ToListAsync(ct);
        var reviewDtos = new List<ConfirmationReviewerReviewDto>();
        foreach (var r in reviews) reviewDtos.Add(await BuildReviewDtoAsync(r, ct));
        return new ConfirmationReviewCaseDetailDto(caseDto, questions, reviewDtos);
    }

    private async Task<ConfirmationReviewCaseDto> BuildCaseDtoAsync(ConfirmationReviewCase c, CancellationToken ct)
    {
        var emp = await _db.Employees.AsNoTracking().Where(e => e.Id == c.EmployeeId)
            .Select(e => new { e.EmployeeCode, Name = e.FirstName + " " + e.LastName }).FirstOrDefaultAsync(ct);
        var questionCount = await _db.ConfirmationReviewCaseQuestions.AsNoTracking().CountAsync(q => q.ConfirmationReviewCaseId == c.Id, ct);
        return new ConfirmationReviewCaseDto(c.Id, c.EmployeeId, emp?.EmployeeCode, emp?.Name,
            c.ConfirmationPolicyId, c.ConfirmationPolicyVersionId, c.ProbationCycleKey, c.ReviewRound,
            c.ProbationStartDate, c.ProbationEndDate, c.DueDate, c.Status,
            c.ManagerReviewerEmployeeId, c.HrbpReviewerEmployeeId, questionCount, c.CreatedDate, c.ModifiedDate);
    }

    private async Task<IReadOnlyList<ConfirmationReviewCaseQuestionDto>> BuildQuestionsAsync(Guid caseId, CancellationToken ct)
    {
        var questions = await _db.ConfirmationReviewCaseQuestions.AsNoTracking()
            .Where(q => q.ConfirmationReviewCaseId == caseId).OrderBy(q => q.DisplayOrder).ThenBy(q => q.Code).ToListAsync(ct);
        var ids = questions.Select(q => q.Id).ToList();
        var options = await _db.ConfirmationReviewCaseQuestionOptions.AsNoTracking().Where(o => ids.Contains(o.ConfirmationReviewCaseQuestionId)).ToListAsync(ct);
        var ratings = await _db.ConfirmationReviewCaseRatingValues.AsNoTracking().Where(v => ids.Contains(v.ConfirmationReviewCaseQuestionId)).ToListAsync(ct);
        return questions.Select(q => new ConfirmationReviewCaseQuestionDto(
            q.Id, q.SourceQuestionId, q.Code, q.QuestionText, q.HelpText, q.QuestionType, q.IsMandatory, q.DisplayOrder,
            q.SourceRatingScaleId, q.RatingScaleCode, q.RatingScaleName, q.RatingMin, q.RatingMax,
            options.Where(o => o.ConfirmationReviewCaseQuestionId == q.Id).OrderBy(o => o.DisplayOrder).ThenBy(o => o.Code)
                .Select(o => new ConfirmationReviewCaseOptionDto(o.Code, o.Label, o.DisplayOrder)).ToList(),
            ratings.Where(v => v.ConfirmationReviewCaseQuestionId == q.Id).OrderBy(v => v.DisplayOrder).ThenBy(v => v.RatingValue)
                .Select(v => new ConfirmationReviewCaseRatingValueDto(v.RatingValue, v.Label, v.DisplayOrder)).ToList()))
            .ToList();
    }

    private async Task<ConfirmationReviewerReviewDto> BuildReviewDtoAsync(ConfirmationReviewerReview review, CancellationToken ct)
    {
        var responses = await _db.ConfirmationReviewResponses.AsNoTracking().Where(r => r.ConfirmationReviewerReviewId == review.Id).ToListAsync(ct);
        var responseIds = responses.Select(r => r.Id).ToList();
        var selected = await _db.ConfirmationReviewResponseOptions.AsNoTracking().Where(o => responseIds.Contains(o.ConfirmationReviewResponseId)).ToListAsync(ct);
        var responseDtos = responses.Select(r => new ConfirmationReviewResponseDto(
            r.ConfirmationReviewCaseQuestionId, r.RatingValue, r.BoolValue, r.TextValue,
            selected.Where(o => o.ConfirmationReviewResponseId == r.Id).Select(o => o.OptionCode).ToList())).ToList();
        return new ConfirmationReviewerReviewDto(review.Id, review.ReviewerType, review.ReviewerEmployeeId, review.ReviewRound, review.VersionNumber, review.Status, review.OverallComments, review.SubmittedAtUtc, responseDtos);
    }

    // --- factories ---

    private ConfirmationReviewerReview NewReview(Guid tenantId, Guid caseId, int reviewRound, ConfirmationReviewerType reviewerType, int version) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewCaseId = caseId, ReviewerType = reviewerType,
        ReviewRound = reviewRound, VersionNumber = version, Status = ConfirmationReviewStatus.Draft, CreatedDate = _time.GetUtcNow().UtcDateTime
    };

    private ConfirmationReviewCaseEvent Event(Guid tenantId, Guid caseId, ConfirmationReviewCaseEventType type, DateTime now, Guid? reviewId, string details) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, ConfirmationReviewCaseId = caseId, ConfirmationReviewerReviewId = reviewId,
        EventType = type, ActorUserId = _tenant.UserId, Details = details, OccurredAtUtc = now, CreatedDate = now
    };

    private static IQueryable<ConfirmationReviewCase> ApplySort(IQueryable<ConfirmationReviewCase> source, ConfirmationReviewCaseQuery query)
    {
        var desc = query.SortDescending;
        var ordered = query.SortBy?.Trim().ToLowerInvariant() switch
        {
            "duedate" => desc ? source.OrderByDescending(x => x.DueDate) : source.OrderBy(x => x.DueDate),
            "probationenddate" => desc ? source.OrderByDescending(x => x.ProbationEndDate) : source.OrderBy(x => x.ProbationEndDate),
            "status" => desc ? source.OrderByDescending(x => x.Status) : source.OrderBy(x => x.Status),
            _ => desc ? source.OrderByDescending(x => x.CreatedDate) : source.OrderBy(x => x.CreatedDate)
        };
        return ordered.ThenBy(x => x.Id);
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
