using System.Linq.Expressions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Phase 9c-3 — confirmation decision engine, final-decision workflow and automatic outcome. Covers approval
/// requirement variants, readiness validation, decision-rule evaluation against the snapshot, each outcome
/// (confirm / extend / not-confirmed / additional review) with employee-side effects and audit, duplicate-decision
/// and closed-case protection, extension cycle identity + resolver/9b compatibility, tenant isolation,
/// authorization and concurrency (exactly one decision wins).
/// </summary>
public sealed class ConfirmationDecisionTests
{
    private static readonly DateOnly Doj = new(2026, 1, 15);
    private static readonly DateOnly AsOf = new(2026, 6, 1);
    private static readonly DateOnly BaselineEnd = new(2026, 7, 15); // Doj + 6 months

    [Fact]
    public async Task Full_confirmation_sequence_reaches_terminal_confirmed_state()
    {
        // Offline Stage 3 rehearsal: the fixture creates the employee, active employment history,
        // published policy, case and real reviewer submissions in SQLite; the real decision service
        // performs preview, Confirmed and persisted-state verification.
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 5, hrbpRating: 5);

        var preview = await h.Decision.CalculateAsync(h.CaseId);
        Assert.Equal(ResultStatus.Success, preview.Status);
        Assert.True(preview.Value!.IsReadyForDecision);
        Assert.Contains(ConfirmationOutcome.Confirmed, preview.Value.PermittedOutcomes);

        var decision = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed, "offline Stage 3 rehearsal"));
        Assert.Equal(ResultStatus.Success, decision.Status);
        Assert.Equal(ConfirmationOutcome.Confirmed, decision.Value!.Outcome);
        Assert.Equal(ConfirmationReviewCaseStatus.Decided, decision.Value.NewStatus);

        var finalCase = await h.Cases.GetCaseAsync(h.CaseId);
        Assert.Equal(ResultStatus.Success, finalCase.Status);
        Assert.Equal(ConfirmationReviewCaseStatus.Decided, finalCase.Value!.Case.Status);
        Assert.DoesNotContain(typeof(ConfirmationReviewCaseDto).GetProperties(), property => property.Name == "Outcome");

        var history = await h.Decision.GetHistoryAsync(h.CaseId);
        Assert.Equal(ResultStatus.Success, history.Status);
        Assert.Contains(history.Value!.Decisions, item => item.Outcome == ConfirmationOutcome.Confirmed);

        using var verify = db.CreateContext(new TestTenantContext(h.Tenant));
        var employment = await verify.EmployeeEmployments.SingleAsync(e => e.EmployeeId == h.Employee);
        var employee = await verify.Employees.SingleAsync(e => e.Id == h.Employee);
        var reviewCase = await verify.ConfirmationReviewCases.SingleAsync(c => c.Id == h.CaseId);
        var cycle = await verify.ConfirmationProbationCycles.SingleAsync(c => c.EmployeeId == h.Employee);
        var decisions = await verify.ConfirmationDecisions.Where(d => d.ConfirmationReviewCaseId == h.CaseId).ToListAsync();
        var events = await verify.ConfirmationReviewCaseEvents.Where(e => e.ConfirmationReviewCaseId == h.CaseId).Select(e => e.EventType).ToListAsync();
        var jobStatusAudits = await verify.EmployeeAuditLogs.Where(a => a.EmployeeId == h.Employee && a.FieldName == "JobStatus").ToListAsync();
        Assert.Equal("Confirmed", employment.JobStatus);
        Assert.Equal(BaselineEnd, employment.ConfirmationDate);
        Assert.Equal("Confirmed", employee.JobStatus);
        Assert.Equal(ConfirmationProbationCycleStatus.Closed, cycle.Status);
        Assert.Equal(ConfirmationReviewCaseStatus.Decided, reviewCase.Status);
        Assert.Single(decisions);
        var jobStatusAudit = Assert.Single(jobStatusAudits);
        Assert.Null(jobStatusAudit.OldValue);
        Assert.Equal("Confirmed", jobStatusAudit.NewValue);
        Assert.Contains(ConfirmationReviewCaseEventType.EmployeeConfirmed, events);
    }

    // ── Confirm ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_sets_confirmation_date_and_job_status_without_touching_doj()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 5, hrbpRating: 5);

        var result = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));

        Assert.Equal(ResultStatus.Success, result.Status);
        Assert.Equal(ConfirmationOutcome.Confirmed, result.Value!.Outcome);
        Assert.Equal(BaselineEnd, result.Value.ConfirmationDate);

        using var verify = db.CreateContext(new TestTenantContext(h.Tenant));
        var employment = await verify.EmployeeEmployments.SingleAsync(e => e.EmployeeId == h.Employee);
        var employee = await verify.Employees.SingleAsync(e => e.Id == h.Employee);
        Assert.Equal(BaselineEnd, employment.ConfirmationDate);
        Assert.Equal("Confirmed", employment.JobStatus);
        Assert.Equal(Doj, employee.DateOfJoining); // DOJ untouched
        var reviewCase = await verify.ConfirmationReviewCases.SingleAsync(c => c.Id == h.CaseId);
        Assert.Equal(ConfirmationReviewCaseStatus.Decided, reviewCase.Status);
    }

    [Fact]
    public async Task Confirm_records_decision_and_audit_history()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 5, hrbpRating: 5);

        await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed, "Strong performer"));
        var history = await h.Decision.GetHistoryAsync(h.CaseId);

        Assert.Single(history.Value!.Decisions);
        Assert.Equal("Strong performer", history.Value.Decisions[0].DecisionReason);
        Assert.Equal(ConfirmationReviewCaseStatus.Completed, history.Value.Decisions[0].PreviousCaseStatus);
        Assert.Equal(ConfirmationReviewCaseStatus.Decided, history.Value.Decisions[0].NewCaseStatus);

        using var verify = db.CreateContext(new TestTenantContext(h.Tenant));
        var events = await verify.ConfirmationReviewCaseEvents.Where(e => e.ConfirmationReviewCaseId == h.CaseId).Select(e => e.EventType).ToListAsync();
        Assert.Contains(ConfirmationReviewCaseEventType.DecisionSubmitted, events);
        Assert.Contains(ConfirmationReviewCaseEventType.EmployeeConfirmed, events);
    }

    [Fact]
    public async Task Duplicate_confirmation_is_rejected()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 5, hrbpRating: 5);

        var first = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));
        var second = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));

        Assert.Equal(ResultStatus.Success, first.Status);
        Assert.Equal(ResultStatus.Conflict, second.Status);
    }

    // ── Decision-rule driven outcomes ────────────────────────────────────────────────

    [Fact]
    public async Task Preview_calculates_outcome_from_snapshot_and_rules()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 3, hrbpRating: 3);

        var preview = await h.Decision.CalculateAsync(h.CaseId);

        Assert.Equal(3m, preview.Value!.OverallRating);
        Assert.Equal(ConfirmationOutcome.ProbationExtended, preview.Value.CalculatedOutcome);
        Assert.True(preview.Value.IsReadyForDecision);
    }

    [Fact]
    public async Task Reject_sets_not_confirmed_job_status_only()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 1, hrbpRating: 1);

        var result = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.NotConfirmed));

        Assert.Equal(ResultStatus.Success, result.Status);
        using var verify = db.CreateContext(new TestTenantContext(h.Tenant));
        var employment = await verify.EmployeeEmployments.SingleAsync(e => e.EmployeeId == h.Employee);
        var employee = await verify.Employees.SingleAsync(e => e.Id == h.Employee);
        Assert.Equal("Not Confirmed", employment.JobStatus);
        Assert.Null(employment.ConfirmationDate);
        Assert.Equal(EmployeeStatus.Active, employee.Status); // not terminated; separation stays separate
    }

    [Fact]
    public async Task Additional_review_keeps_case_active_and_changes_no_employee_data()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 5, hrbpRating: 5);

        var result = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.AdditionalReview, "Need another cycle"));

        Assert.Equal(ResultStatus.Success, result.Status);
        using var verify = db.CreateContext(new TestTenantContext(h.Tenant));
        var reviewCase = await verify.ConfirmationReviewCases.SingleAsync(c => c.Id == h.CaseId);
        Assert.Equal(ConfirmationReviewCaseStatus.InReview, reviewCase.Status);
        Assert.Equal(ConfirmationOutcome.Pending, reviewCase.Outcome);
        Assert.Equal(2, reviewCase.ReviewRound);
        var decision = await verify.ConfirmationDecisions.SingleAsync(d => d.ConfirmationReviewCaseId == h.CaseId);
        Assert.Equal(1, decision.ReviewRound);
        Assert.False(await verify.EmployeeEmployments.AnyAsync(e => e.EmployeeId == h.Employee && e.ConfirmationDate != null));
    }

    // ── Extension ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Extend_creates_new_cycle_with_new_end_and_resolver_returns_it()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 3, hrbpRating: 3, maxExtensions: 2, extensionMonths: 3);

        var result = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.ProbationExtended, "One more cycle"));

        Assert.Equal(ResultStatus.Success, result.Status);
        var newEnd = new DateOnly(2026, 10, 15); // BaselineEnd + 3 months
        Assert.Equal(newEnd, result.Value!.NewProbationEndDate);
        Assert.Equal(2, result.Value.NewCycleNumber);
        Assert.NotNull(result.Value.NewCaseId);

        using var verify = db.CreateContext(new TestTenantContext(h.Tenant));
        // Old cycle preserved (closed); new cycle active.
        var cycles = await verify.ConfirmationProbationCycles.Where(c => c.EmployeeId == h.Employee).OrderBy(c => c.CycleNumber).ToListAsync();
        Assert.Equal(2, cycles.Count);
        Assert.Equal(ConfirmationProbationCycleStatus.Closed, cycles[0].Status);
        Assert.Equal(ConfirmationProbationCycleStatus.Active, cycles[1].Status);
        Assert.Equal(newEnd, cycles[1].CurrentEndDate);
        Assert.Single(await verify.ConfirmationProbationExtensions.Where(e => e.EmployeeId == h.Employee).ToListAsync());

        // 9b compatibility: the resolver now reports the new probation-end, so reminder dedup keys move to the new cycle.
        var resolver = new ConfirmationPolicyResolver(verify, tenantContext: new TestTenantContext(h.Tenant));
        var resolved = await resolver.ResolveAsync(h.Tenant, h.Employee, AsOf);
        Assert.Equal(newEnd, resolved.ProbationEndDate);
    }

    [Fact]
    public async Task Extend_beyond_max_extensions_is_rejected()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 3, hrbpRating: 3, maxExtensions: 1, extensionMonths: 3);

        var first = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.ProbationExtended));
        Assert.Equal(ResultStatus.Success, first.Status);

        // Drive the NEW cycle's case to completion and try to extend again → blocked by MaxExtensions.
        var newCaseId = first.Value!.NewCaseId!.Value;
        await SubmitReviewAsync(h, newCaseId, ConfirmationReviewerType.ReportingManager, 3);
        await SubmitReviewAsync(h, newCaseId, ConfirmationReviewerType.Hrbp, 3);

        var second = await h.Decision.SubmitDecisionAsync(newCaseId, new(ConfirmationOutcome.ProbationExtended));
        Assert.Equal(ResultStatus.ValidationFailed, second.Status);
    }

    // ── Approval requirements / readiness ────────────────────────────────────────────

    [Fact]
    public async Task Manager_only_policy_is_decidable_with_one_review()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCaseAsync(db, requiredApproval: ConfirmationApprovalRequirement.Manager);
        await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.ReportingManager, 5);

        var result = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));
        Assert.Equal(ResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task Parallel_approval_allows_hrbp_to_submit_before_manager()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCaseAsync(db, requiredApproval: ConfirmationApprovalRequirement.ManagerAndHrbp, sequence: ApprovalSequence.Parallel);

        Assert.Equal(ResultStatus.Success, (await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.Hrbp, 5)).Status);
        Assert.Equal(ResultStatus.Success, (await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.ReportingManager, 5)).Status);

        var decision = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));
        Assert.Equal(ResultStatus.Success, decision.Status);
    }

    [Fact]
    public async Task Hrbp_only_policy_is_decidable_with_only_an_hrbp_review()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCaseAsync(db, requiredApproval: ConfirmationApprovalRequirement.Hrbp);

        Assert.Equal(ResultStatus.Success, (await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.Hrbp, 5)).Status);
        var decision = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));

        Assert.Equal(ResultStatus.Success, decision.Status);
    }

    [Fact]
    public async Task Manager_or_hrbp_policy_is_decidable_after_either_reviewer_submits()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCaseAsync(db, requiredApproval: ConfirmationApprovalRequirement.ManagerOrHrbp);

        Assert.Equal(ResultStatus.Success, (await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.ReportingManager, 5)).Status);
        var decision = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));

        Assert.Equal(ResultStatus.Success, decision.Status);
    }

    [Fact]
    public async Task Both_required_blocks_decision_until_both_reviews_submitted()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCaseAsync(db, requiredApproval: ConfirmationApprovalRequirement.ManagerAndHrbp);
        await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.ReportingManager, 5);

        var tooEarly = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));
        Assert.Equal(ResultStatus.Conflict, tooEarly.Status); // case not yet Completed

        await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.Hrbp, 5);
        var ok = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));
        Assert.Equal(ResultStatus.Success, ok.Status);
    }

    [Fact]
    public async Task Approval_sequence_blocks_hrbp_before_manager()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCaseAsync(db, requiredApproval: ConfirmationApprovalRequirement.ManagerAndHrbp, sequence: ApprovalSequence.ManagerThenHrbp);

        var hrbpFirst = await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.Hrbp, 5);
        Assert.Equal(ResultStatus.Conflict, hrbpFirst.Status);

        Assert.Equal(ResultStatus.Success, (await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.ReportingManager, 5)).Status);
        Assert.Equal(ResultStatus.Success, (await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.Hrbp, 5)).Status);
    }

    [Fact]
    public async Task Required_reviewer_comments_block_decision_when_missing()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCaseAsync(db, requireComments: true);
        await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.ReportingManager, 5); // no comments
        await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.Hrbp, 5);

        var blocked = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));
        Assert.Equal(ResultStatus.ValidationFailed, blocked.Status);
    }

    [Fact]
    public async Task Outcome_not_permitted_by_rules_is_rejected()
    {
        using var db = new SqliteInMemoryDatabase();
        // No extension configured → ProbationExtended is not a permitted outcome.
        var h = await SeedCompletedCaseAsync(db, managerRating: 5, hrbpRating: 5, maxExtensions: 0);

        var result = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.ProbationExtended));
        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    // ── Security ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Decider_without_access_is_forbidden()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 5, hrbpRating: 5);
        h.Access.AllowAll = false;

        var result = await h.Decision.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));
        Assert.Equal(ResultStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task Decisions_are_tenant_isolated()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 5, hrbpRating: 5);

        var tenantB = Guid.NewGuid();
        using var ctxB = db.CreateContext(new TestTenantContext(tenantB));
        ctxB.Tenants.Add(NewTenant(tenantB)); await ctxB.SaveChangesAsync();
        var (decisionB, _) = BuildServices(ctxB, tenantB, new FakeAccessScope());

        var preview = await decisionB.CalculateAsync(h.CaseId);
        Assert.Equal(ResultStatus.NotFound, preview.Status);
    }

    // ── Concurrency ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Only_one_of_two_decisions_on_the_same_case_wins()
    {
        using var db = new SqliteInMemoryDatabase();
        var h = await SeedCompletedCaseAsync(db, managerRating: 5, hrbpRating: 5);

        // Two independent contexts/connections over the same in-memory DB — the DB-level compare-and-swap, not
        // just in-memory tracking, must reject the loser.
        using var ctxA = db.CreateIsolatedContext(new TestTenantContext(h.Tenant, Guid.NewGuid()));
        using var ctxB = db.CreateIsolatedContext(new TestTenantContext(h.Tenant, Guid.NewGuid()));
        var (decA, _) = BuildServices(ctxA, h.Tenant, new FakeAccessScope());
        var (decB, _) = BuildServices(ctxB, h.Tenant, new FakeAccessScope());

        var rA = await decA.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));
        var rB = await decB.SubmitDecisionAsync(h.CaseId, new(ConfirmationOutcome.Confirmed));

        var successes = new[] { rA, rB }.Count(r => r.Status == ResultStatus.Success);
        var conflicts = new[] { rA, rB }.Count(r => r.Status == ResultStatus.Conflict);
        Assert.Equal(1, successes);
        Assert.Equal(1, conflicts);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────────

    private sealed record Handle(
        IConfirmationDecisionService Decision, IConfirmationReviewCaseService Cases, Guid CaseId,
        Guid Tenant, Guid Employee, Guid RatingQuestionId, FakeAccessScope Access, SqliteInMemoryDatabase Db);

    private static (IConfirmationDecisionService Decision, IConfirmationReviewCaseService Cases) BuildServices(HrmsDbContext ctx, Guid tenant, FakeAccessScope access)
    {
        var tc = new TestTenantContext(tenant, Guid.NewGuid());
        var cases = new ConfirmationReviewCaseService(ctx, tc,
            new ConfirmationPolicyResolver(ctx, tenantContext: tc),
            new ConfirmationReviewQuestionResolver(ctx, tenantContext: tc),
            new FakeHrbpResolver(), new FakeManagerResolver(), access);
        var jobStatus = new JobStatusService(ctx, tc,
            new ConfirmationAuthorization(), TimeProvider.System);
        var decision = new ConfirmationDecisionService(ctx, tc, access, cases, jobStatus);
        return (decision, cases);
    }

    private sealed class ConfirmationAuthorization : ICurrentAuthorizationContext
    {
        public bool HasAnyPermission(params string[] requested) => requested.Contains(Permissions.Confirmation.Decide);
    }

    private static async Task<Handle> SeedCaseAsync(
        SqliteInMemoryDatabase db,
        ConfirmationApprovalRequirement requiredApproval = ConfirmationApprovalRequirement.ManagerAndHrbp,
        ApprovalSequence sequence = ApprovalSequence.Parallel,
        bool requireComments = false, int maxExtensions = 0, int extensionMonths = 0)
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee);
        var versionId = await AddPublishedPolicyAsync(ctx, tenant, requiredApproval, sequence, requireComments, maxExtensions, extensionMonths);
        await AddActiveProbationCycleAsync(ctx, tenant, employee, versionId);
        var scaleId = await AddRatingScaleAsync(ctx, tenant);
        var ratingQuestionId = await AddRatingQuestionAsync(ctx, tenant, scaleId);

        var access = new FakeAccessScope();
        var (decision, cases) = BuildServices(ctx, tenant, access);
        var created = await cases.CreateCaseAsync(new(employee, AsOf));
        Assert.Equal(ResultStatus.Success, created.Status);
        return new Handle(decision, cases, created.Value!.Case.Id, tenant, employee, ratingQuestionId, access, db);
    }

    private static async Task<Handle> SeedCompletedCaseAsync(SqliteInMemoryDatabase db, int managerRating, int hrbpRating, int maxExtensions = 1, int extensionMonths = 3)
    {
        var h = await SeedCaseAsync(db, maxExtensions: maxExtensions, extensionMonths: extensionMonths);
        Assert.Equal(ResultStatus.Success, (await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.ReportingManager, managerRating)).Status);
        Assert.Equal(ResultStatus.Success, (await SubmitReviewAsync(h, h.CaseId, ConfirmationReviewerType.Hrbp, hrbpRating)).Status);
        return h;
    }

    private static Task<Result<ConfirmationReviewerReviewDto>> SubmitReviewAsync(Handle h, Guid caseId, ConfirmationReviewerType reviewer, int rating) =>
        h.Cases.SubmitReviewAsync(caseId, reviewer, new(OverallComments: null,
            Responses: new[] { new ConfirmationReviewResponseInput(RatingQuestionIdFor(h, caseId), RatingValue: rating) }));

    // Resolve the snapshot rating-question id for a given case (the snapshot id differs per case).
    private static Guid RatingQuestionIdFor(Handle h, Guid caseId)
    {
        using var ctx = h.Db.CreateContext(new TestTenantContext(h.Tenant));
        return ctx.ConfirmationReviewCaseQuestions.AsNoTracking()
            .First(q => q.ConfirmationReviewCaseId == caseId && q.QuestionType == ReviewQuestionType.Rating).Id;
    }

    private static Tenant NewTenant(Guid tenant) => new()
    {
        Id = tenant, TenantCode = Guid.NewGuid().ToString("N")[..8], Host = Guid.NewGuid().ToString("N") + ".local",
        ShardKey = Guid.NewGuid().ToString("N"), TenantName = "Test"
    };

    private static async Task AddEmployeeAsync(HrmsDbContext db, Guid tenant, Guid employee)
    {
        db.Tenants.Add(NewTenant(tenant));
        db.Employees.Add(new Employee { Id = employee, TenantId = tenant, FirstName = "Test", LastName = "Employee", Email = Guid.NewGuid() + "@test.local", DateOfJoining = Doj, Gender = Gender.Unspecified, Status = EmployeeStatus.Active });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = Doj });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> AddPublishedPolicyAsync(
        HrmsDbContext db, Guid tenant, ConfirmationApprovalRequirement requiredApproval, ApprovalSequence sequence,
        bool requireComments, int maxExtensions, int extensionMonths)
    {
        var policy = new ConfirmationPolicy { Id = Guid.NewGuid(), TenantId = tenant, Code = Guid.NewGuid().ToString("N")[..8], Name = "Policy", IsActive = true };
        var version = new ConfirmationPolicyVersion { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyId = policy.Id, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = ConfirmationPolicyVersionStatus.Published, Priority = 1 };
        var rule = new ConfirmationPolicyRule
        {
            Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id,
            ProbationDuration = 6, ProbationDurationUnit = ProbationDurationUnit.Months, TriggerDaysBeforeEnd = 30,
            MaxExtensions = maxExtensions, ExtensionDuration = extensionMonths, ExtensionDurationUnit = ProbationDurationUnit.Months,
            RequiredApproval = requiredApproval, ApprovalSequence = sequence, RequireReviewerComments = requireComments,
            OverallRatingMethod = OverallRatingMethod.AverageOfRatings
        };
        db.ConfirmationPolicies.Add(policy); db.ConfirmationPolicyVersions.Add(version); db.ConfirmationPolicyRules.Add(rule);
        db.ConfirmationDecisionRules.Add(new ConfirmationDecisionRule { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id, Priority = 1, Name = "Confirm", MinOverallRating = 4, MaxOverallRating = null, Outcome = ConfirmationOutcome.Confirmed, IsActive = true });
        db.ConfirmationDecisionRules.Add(new ConfirmationDecisionRule { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id, Priority = 2, Name = "Extend", MinOverallRating = 3, MaxOverallRating = 3.99m, Outcome = ConfirmationOutcome.ProbationExtended, IsActive = true });
        db.ConfirmationDecisionRules.Add(new ConfirmationDecisionRule { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id, Priority = 3, Name = "Reject", MinOverallRating = null, MaxOverallRating = 2.99m, Outcome = ConfirmationOutcome.NotConfirmed, IsActive = true });
        await db.SaveChangesAsync();
        return version.Id;
    }

    private static async Task AddActiveProbationCycleAsync(HrmsDbContext db, Guid tenant, Guid employee, Guid versionId)
    {
        var version = await db.ConfirmationPolicyVersions.SingleAsync(v => v.Id == versionId);
        db.ConfirmationProbationCycles.Add(new ConfirmationProbationCycle
        {
            Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee,
            ConfirmationPolicyId = version.ConfirmationPolicyId, ConfirmationPolicyVersionId = versionId,
            CycleNumber = 1, StartDate = Doj, BaselineEndDate = BaselineEnd, CurrentEndDate = BaselineEnd,
            Status = ConfirmationProbationCycleStatus.Active, CreatedDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> AddRatingScaleAsync(HrmsDbContext db, Guid tenant)
    {
        var scale = new ConfirmationRatingScale { Id = Guid.NewGuid(), TenantId = tenant, Code = "P5", Name = "Five", MinRating = 1, MaxRating = 5, IsActive = true };
        db.ConfirmationRatingScales.Add(scale);
        for (var i = 1; i <= 5; i++)
            db.ConfirmationRatingScaleValues.Add(new ConfirmationRatingScaleValue { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationRatingScaleId = scale.Id, RatingValue = i, Label = "L" + i, DisplayOrder = i, IsActive = true });
        await db.SaveChangesAsync();
        return scale.Id;
    }

    private static async Task<Guid> AddRatingQuestionAsync(HrmsDbContext db, Guid tenant, Guid scaleId)
    {
        var q = new ConfirmationReviewQuestion { Id = Guid.NewGuid(), TenantId = tenant, Code = "QR", QuestionText = "Overall", QuestionType = ReviewQuestionType.Rating, IsMandatory = true, IsActive = true, DisplayOrder = 1, ConfirmationRatingScaleId = scaleId };
        db.ConfirmationReviewQuestions.Add(q);
        await db.SaveChangesAsync();
        return q.Id;
    }

    // ── Test doubles ─────────────────────────────────────────────────────────────────

    private sealed class FakeAccessScope : IEmployeeAccessScopeService
    {
        public bool AllowAll { get; set; } = true;
        public Task<Expression<Func<Employee, bool>>> BuildPredicateAsync(DateOnly effectiveDate, CancellationToken ct = default)
            => Task.FromResult<Expression<Func<Employee, bool>>>(AllowAll ? _ => true : _ => false);
        public Task<Expression<Func<Employee, bool>>> BuildRoleScopePredicateAsync(DateOnly effectiveDate, CancellationToken ct = default)
            => Task.FromResult<Expression<Func<Employee, bool>>>(AllowAll ? _ => true : _ => false);
        public Task<bool> CanAccessEmployeeAsync(Guid employeeId, DateOnly effectiveDate, CancellationToken ct = default)
            => Task.FromResult(AllowAll);
    }

    private sealed class FakeManagerResolver : IEmployeeManagerResolver
    {
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken ct = default)
            => Task.FromResult(Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.NoAssignedManager, employeeId, null, null, null, "no manager")));
        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class FakeHrbpResolver : IConfirmationHrbpResolver
    {
        public Task<ConfirmationHrbpResolutionResult> ResolveAsync(Guid tenantId, Guid employeeId, DateOnly effectiveDate, CancellationToken ct = default)
            => Task.FromResult(new ConfirmationHrbpResolutionResult(ConfirmationHrbpResolutionStatus.NoHrbp, null, "none"));
    }
}
