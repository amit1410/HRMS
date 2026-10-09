using System.Linq.Expressions;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Confirmation;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// Phase 9c-2 — Confirmation review case, question resolution, snapshotting and reviewer reviews. Covers the
/// question resolver (specificity/precedence, effective dates, generic vs scoped vs policy-scoped), the HRBP
/// role-scope resolver, case creation + idempotency, snapshot immutability, response validation per snapshot
/// type, draft/submit, submitted-review immutability, reviewer authorization, and tenant isolation.
/// </summary>
public sealed class ConfirmationReviewCaseTests
{
    private static readonly DateOnly Doj = new(2026, 1, 15);
    private static readonly DateOnly AsOf = new(2026, 6, 1);

    // ── Question resolver ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolver_includes_generic_question_and_orders_by_display_order()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddQuestionAsync(ctx, tenant, "Q2", ReviewQuestionType.Text, displayOrder: 2);
        await AddQuestionAsync(ctx, tenant, "Q1", ReviewQuestionType.YesNo, displayOrder: 1);

        var result = await new ConfirmationReviewQuestionResolver(ctx, tenantContext: new TestTenantContext(tenant))
            .ResolveAsync(tenant, employee, AsOf, null);

        Assert.Equal(ConfirmationReviewQuestionResolutionStatus.Resolved, result.Status);
        Assert.Equal(new[] { "Q1", "Q2" }, result.Questions.Select(q => q.Code).ToArray());
    }

    [Fact]
    public async Task Resolver_matches_scoped_question_only_for_matching_dimension()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var dept = Guid.NewGuid();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee, dept);
        var otherDept = Guid.NewGuid();
        ctx.Departments.Add(new Department { Id = otherDept, TenantId = tenant, Code = "D" + Guid.NewGuid().ToString("N")[..6], Name = "Other" });
        await ctx.SaveChangesAsync();
        await AddQuestionAsync(ctx, tenant, "DEPT", ReviewQuestionType.Text, applicability: s => s.DepartmentId = dept);
        await AddQuestionAsync(ctx, tenant, "OTHER", ReviewQuestionType.Text, applicability: s => s.DepartmentId = otherDept);

        var result = await new ConfirmationReviewQuestionResolver(ctx, tenantContext: new TestTenantContext(tenant))
            .ResolveAsync(tenant, employee, AsOf, null);

        Assert.Equal(new[] { "DEPT" }, result.Questions.Select(q => q.Code).ToArray());
    }

    [Fact]
    public async Task Resolver_includes_policy_scoped_question_only_for_resolved_policy()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var policyId = Guid.NewGuid();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee);
        ctx.ConfirmationPolicies.Add(new ConfirmationPolicy { Id = policyId, TenantId = tenant, Code = "POL" + Guid.NewGuid().ToString("N")[..6], Name = "Policy", IsActive = true });
        await ctx.SaveChangesAsync();
        await AddQuestionAsync(ctx, tenant, "POL", ReviewQuestionType.Text, applicability: s => s.ConfirmationPolicyId = policyId);

        var forOther = await new ConfirmationReviewQuestionResolver(ctx, tenantContext: new TestTenantContext(tenant)).ResolveAsync(tenant, employee, AsOf, Guid.NewGuid());
        var forPolicy = await new ConfirmationReviewQuestionResolver(ctx, tenantContext: new TestTenantContext(tenant)).ResolveAsync(tenant, employee, AsOf, policyId);

        Assert.Empty(forOther.Questions);
        Assert.Single(forPolicy.Questions);
    }

    [Fact]
    public async Task Resolver_excludes_inactive_and_out_of_effective_range_questions()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddQuestionAsync(ctx, tenant, "INACTIVE", ReviewQuestionType.Text, isActive: false);
        await AddQuestionAsync(ctx, tenant, "FUTURE", ReviewQuestionType.Text, effectiveFrom: new(2027, 1, 1));
        await AddQuestionAsync(ctx, tenant, "LIVE", ReviewQuestionType.Text);

        var result = await new ConfirmationReviewQuestionResolver(ctx, tenantContext: new TestTenantContext(tenant))
            .ResolveAsync(tenant, employee, AsOf, null);

        Assert.Equal(new[] { "LIVE" }, result.Questions.Select(q => q.Code).ToArray());
    }

    // ── HRBP resolver ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hrbp_resolver_resolves_tenant_wide_assignment()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee);
        var hrbpEmployee = await AddHrbpUserAsync(ctx, tenant);

        var result = await new ConfirmationHrbpResolver(ctx, tenantContext: new TestTenantContext(tenant)).ResolveAsync(tenant, employee, AsOf);

        Assert.Equal(ConfirmationHrbpResolutionStatus.Resolved, result.Status);
        Assert.Equal(hrbpEmployee, result.HrbpEmployeeId);
    }

    [Fact]
    public async Task Hrbp_resolver_requires_scope_to_cover_employee()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var dept = Guid.NewGuid();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee, dept);
        await AddHrbpUserAsync(ctx, tenant, scopeDepartment: Guid.NewGuid()); // different dept

        var result = await new ConfirmationHrbpResolver(ctx, tenantContext: new TestTenantContext(tenant)).ResolveAsync(tenant, employee, AsOf);

        Assert.Equal(ConfirmationHrbpResolutionStatus.NoHrbp, result.Status);
    }

    [Fact]
    public async Task Hrbp_resolver_reports_ambiguity_for_two_equally_specific_hrbps()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddHrbpUserAsync(ctx, tenant);
        await AddHrbpUserAsync(ctx, tenant);

        var result = await new ConfirmationHrbpResolver(ctx, tenantContext: new TestTenantContext(tenant)).ResolveAsync(tenant, employee, AsOf);

        Assert.Equal(ConfirmationHrbpResolutionStatus.Ambiguous, result.Status);
    }

    // ── Case creation / idempotency / snapshot ───────────────────────────────────────

    [Fact]
    public async Task Create_case_snapshots_questions_and_is_idempotent()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, 1, 6);
        await AddQuestionAsync(ctx, tenant, "Q1", ReviewQuestionType.YesNo, isMandatory: true);
        var service = Service(ctx, tenant);

        var first = await service.CreateCaseAsync(new(employee, AsOf));
        var second = await service.CreateCaseAsync(new(employee, AsOf));

        Assert.Equal(ResultStatus.Success, first.Status);
        Assert.Single(first.Value!.Questions);
        Assert.Equal(first.Value.Case.Id, second.Value!.Case.Id); // idempotent
        Assert.Equal(new DateOnly(2026, 7, 15), first.Value.Case.ProbationEndDate);
    }

    [Fact]
    public async Task Case_creation_requires_a_resolvable_policy()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee);
        var service = Service(ctx, tenant);

        var result = await service.CreateCaseAsync(new(employee, AsOf));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Snapshot_is_immutable_when_master_question_changes_later()
    {
        using var db = new SqliteInMemoryDatabase();
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        using var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, 1, 6);
        var questionId = await AddQuestionAsync(ctx, tenant, "Q1", ReviewQuestionType.Text, text: "Quality of Work");
        var service = Service(ctx, tenant);
        var created = await service.CreateCaseAsync(new(employee, AsOf));

        // Change the master AFTER the case exists.
        var master = await ctx.ConfirmationReviewQuestions.SingleAsync(q => q.Id == questionId);
        master.QuestionText = "Technical Quality of Work";
        await ctx.SaveChangesAsync();

        var reloaded = await service.GetCaseAsync(created.Value!.Case.Id);
        Assert.Equal("Quality of Work", reloaded.Value!.Questions.Single().QuestionText);
    }

    // ── Validation ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Submit_enforces_mandatory_but_draft_does_not()
    {
        using var db = new SqliteInMemoryDatabase();
        var (service, caseId, _, _, _, _) = await SeedCaseAsync(db, mandatory: true);

        var draft = await service.SaveDraftAsync(caseId, ConfirmationReviewerType.ReportingManager, new());
        Assert.Equal(ResultStatus.Success, draft.Status);

        var submit = await service.SubmitReviewAsync(caseId, ConfirmationReviewerType.ReportingManager, new());
        Assert.Equal(ResultStatus.ValidationFailed, submit.Status);
    }

    [Fact]
    public async Task Submit_validates_rating_against_snapshot_scale()
    {
        using var db = new SqliteInMemoryDatabase();
        var (service, caseId, questions, _, _, _) = await SeedCaseAsync(db, withRating: true);
        var rating = questions.Single(q => q.QuestionType == ReviewQuestionType.Rating);

        var bad = await service.SubmitReviewAsync(caseId, ConfirmationReviewerType.ReportingManager, new(
            Responses: new[] { new ConfirmationReviewResponseInput(rating.Id, RatingValue: 99) }));
        Assert.Equal(ResultStatus.ValidationFailed, bad.Status);

        var ok = await service.SubmitReviewAsync(caseId, ConfirmationReviewerType.ReportingManager, new(
            Responses: new[] { new ConfirmationReviewResponseInput(rating.Id, RatingValue: 3) }));
        Assert.Equal(ResultStatus.Success, ok.Status);
    }

    [Fact]
    public async Task Submit_rejects_duplicate_answers_for_the_same_question()
    {
        using var db = new SqliteInMemoryDatabase();
        var (service, caseId, questions, _, _, _) = await SeedCaseAsync(db, withRating: true);
        var rating = questions.Single(q => q.QuestionType == ReviewQuestionType.Rating);

        var result = await service.SubmitReviewAsync(caseId, ConfirmationReviewerType.ReportingManager, new(
            Responses:
            [
                new ConfirmationReviewResponseInput(rating.Id, RatingValue: 3),
                new ConfirmationReviewResponseInput(rating.Id, RatingValue: 5)
            ]));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
        Assert.Contains(result.Errors, error => error.Field == "responses");
    }

    [Fact]
    public async Task Submit_validates_dropdown_option_against_snapshot()
    {
        using var db = new SqliteInMemoryDatabase();
        var (service, caseId, questions, _, _, _) = await SeedCaseAsync(db, withDropdown: true);
        var dd = questions.Single(q => q.QuestionType == ReviewQuestionType.Dropdown);

        var bad = await service.SubmitReviewAsync(caseId, ConfirmationReviewerType.ReportingManager, new(
            Responses: new[] { new ConfirmationReviewResponseInput(dd.Id, SelectedOptionCodes: new[] { "NOPE" }) }));
        Assert.Equal(ResultStatus.ValidationFailed, bad.Status);

        var ok = await service.SubmitReviewAsync(caseId, ConfirmationReviewerType.ReportingManager, new(
            Responses: new[] { new ConfirmationReviewResponseInput(dd.Id, SelectedOptionCodes: new[] { "YES" }) }));
        Assert.Equal(ResultStatus.Success, ok.Status);
    }

    // ── Submission immutability + history ────────────────────────────────────────────

    [Fact]
    public async Task Resubmission_supersedes_prior_version_and_preserves_history()
    {
        using var db = new SqliteInMemoryDatabase();
        var (service, caseId, _, _, _, _) = await SeedCaseAsync(db);

        var v1 = await service.SubmitReviewAsync(caseId, ConfirmationReviewerType.ReportingManager, new(OverallComments: "first"));
        var v2 = await service.SubmitReviewAsync(caseId, ConfirmationReviewerType.ReportingManager, new(OverallComments: "corrected"));

        Assert.Equal(1, v1.Value!.VersionNumber);
        Assert.Equal(2, v2.Value!.VersionNumber);

        var detail = await service.GetCaseAsync(caseId);
        var reviews = detail.Value!.Reviews.Where(r => r.ReviewerType == ConfirmationReviewerType.ReportingManager).ToList();
        Assert.Contains(reviews, r => r.VersionNumber == 1 && r.Status == ConfirmationReviewStatus.Superseded);
        Assert.Contains(reviews, r => r.VersionNumber == 2 && r.Status == ConfirmationReviewStatus.Submitted);

        var history = await service.GetHistoryAsync(caseId);
        Assert.Contains(history.Value!, e => e.EventType == ConfirmationReviewCaseEventType.ReviewSuperseded);
        Assert.Contains(history.Value!, e => e.EventType == ConfirmationReviewCaseEventType.ReviewSubmitted);
    }

    // ── Authorization + tenant isolation ─────────────────────────────────────────────

    [Fact]
    public async Task Reviewer_without_access_is_forbidden()
    {
        using var db = new SqliteInMemoryDatabase();
        var seeded = await SeedCaseAsync(db);
        seeded.Access.AllowAll = false;

        var submit = await seeded.Service.SubmitReviewAsync(seeded.CaseId, ConfirmationReviewerType.ReportingManager, new(OverallComments: "x"));
        Assert.Equal(ResultStatus.Forbidden, submit.Status);
    }

    [Fact]
    public async Task User_cannot_submit_for_an_assigned_reviewer_without_matching_employee_link()
    {
        using var db = new SqliteInMemoryDatabase();
        var seeded = await SeedCaseAsync(db);
        var reviewCase = await seeded.Ctx.ConfirmationReviewCases.SingleAsync(x => x.Id == seeded.CaseId);
        reviewCase.ManagerReviewerEmployeeId = reviewCase.EmployeeId;
        await seeded.Ctx.SaveChangesAsync();

        var result = await seeded.Service.SubmitReviewAsync(
            seeded.CaseId, ConfirmationReviewerType.ReportingManager, new(OverallComments: "spoofed"));

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Empty(await seeded.Ctx.ConfirmationReviewerReviews.ToListAsync());
    }

    [Fact]
    public async Task Cases_are_tenant_isolated()
    {
        using var db = new SqliteInMemoryDatabase();
        var seeded = await SeedCaseAsync(db);
        var caseId = seeded.CaseId;

        var tenantB = Guid.NewGuid();
        using var ctxB = db.CreateContext(new TestTenantContext(tenantB));
        ctxB.Tenants.Add(NewTenant(tenantB)); await ctxB.SaveChangesAsync();
        var serviceB = Service(ctxB, tenantB);

        var fetch = await serviceB.GetCaseAsync(caseId);
        var list = await serviceB.GetCasesAsync(new ConfirmationReviewCaseQuery());
        Assert.Equal(ResultStatus.NotFound, fetch.Status);
        Assert.Empty(list.Value!.Items);
    }

    // ── Fixtures / helpers ───────────────────────────────────────────────────────

    private static ConfirmationReviewCaseService Service(HrmsDbContext ctx, Guid tenant, FakeAccessScope? access = null)
    {
        var tc = new TestTenantContext(tenant, Guid.NewGuid());
        return new ConfirmationReviewCaseService(
            ctx, tc,
            new ConfirmationPolicyResolver(ctx, tenantContext: tc),
            new ConfirmationReviewQuestionResolver(ctx, tenantContext: tc),
            new FakeHrbpResolver(),
            new FakeManagerResolver(),
            access ?? new FakeAccessScope());
    }

    private sealed record Seeded(
        ConfirmationReviewCaseService Service, Guid CaseId, IReadOnlyList<ConfirmationReviewCaseQuestionDto> Questions,
        FakeAccessScope Access, HrmsDbContext Ctx, Guid Tenant);

    private static async Task<Seeded> SeedCaseAsync(SqliteInMemoryDatabase db, bool withRating = false, bool withDropdown = false, bool mandatory = false)
    {
        var tenant = Guid.NewGuid(); var employee = Guid.NewGuid();
        var ctx = db.CreateContext(new TestTenantContext(tenant));
        await AddEmployeeAsync(ctx, tenant, employee);
        await AddPublishedPolicyAsync(ctx, tenant, 1, 6);
        await AddQuestionAsync(ctx, tenant, "Q1", ReviewQuestionType.Text, isMandatory: mandatory);
        if (withRating)
        {
            var scaleId = await AddRatingScaleAsync(ctx, tenant);
            await AddQuestionAsync(ctx, tenant, "QR", ReviewQuestionType.Rating, ratingScaleId: scaleId);
        }
        if (withDropdown)
            await AddQuestionAsync(ctx, tenant, "QD", ReviewQuestionType.Dropdown, options: new[] { ("YES", "Yes"), ("NO", "No") });

        var access = new FakeAccessScope();
        var service = Service(ctx, tenant, access);
        var created = await service.CreateCaseAsync(new(employee, AsOf));
        return new Seeded(service, created.Value!.Case.Id, created.Value.Questions, access, ctx, tenant);
    }

    private static Tenant NewTenant(Guid tenant) => new()
    {
        Id = tenant, TenantCode = Guid.NewGuid().ToString("N")[..8], Host = Guid.NewGuid().ToString("N") + ".local",
        ShardKey = Guid.NewGuid().ToString("N"), TenantName = "Test"
    };

    private static async Task AddEmployeeAsync(HrmsDbContext db, Guid tenant, Guid employee, Guid? department = null)
    {
        db.Tenants.Add(NewTenant(tenant));
        db.Employees.Add(new Employee { Id = employee, TenantId = tenant, FirstName = "Test", LastName = "Employee", Email = Guid.NewGuid() + "@test.local", DateOfJoining = Doj, Gender = Gender.Unspecified });
        if (department is Guid dept)
            db.Departments.Add(new Department { Id = dept, TenantId = tenant, Code = "D" + Guid.NewGuid().ToString("N")[..6], Name = "Dept" });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = Doj, DepartmentId = department });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> AddPublishedPolicyAsync(HrmsDbContext db, Guid tenant, int priority, int probationMonths)
    {
        var policy = new ConfirmationPolicy { Id = Guid.NewGuid(), TenantId = tenant, Code = Guid.NewGuid().ToString("N")[..8], Name = "Policy", IsActive = true };
        var version = new ConfirmationPolicyVersion { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyId = policy.Id, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = ConfirmationPolicyVersionStatus.Published, Priority = priority };
        var rule = new ConfirmationPolicyRule { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id, ProbationDuration = probationMonths, ProbationDurationUnit = ProbationDurationUnit.Months, TriggerDaysBeforeEnd = 30 };
        db.ConfirmationPolicies.Add(policy); db.ConfirmationPolicyVersions.Add(version); db.ConfirmationPolicyRules.Add(rule);
        await db.SaveChangesAsync();
        return version.Id;
    }

    private static async Task<Guid> AddQuestionAsync(
        HrmsDbContext db, Guid tenant, string code, ReviewQuestionType type, string? text = null, bool isMandatory = false,
        bool isActive = true, int displayOrder = 0, DateOnly? effectiveFrom = null, Guid? ratingScaleId = null,
        (string Code, string Label)[]? options = null, Action<ConfirmationReviewQuestionApplicabilitySet>? applicability = null)
    {
        var q = new ConfirmationReviewQuestion
        {
            Id = Guid.NewGuid(), TenantId = tenant, Code = code, QuestionText = text ?? code, QuestionType = type,
            IsMandatory = isMandatory, IsActive = isActive, DisplayOrder = displayOrder, EffectiveFrom = effectiveFrom,
            ConfirmationRatingScaleId = type == ReviewQuestionType.Rating ? ratingScaleId : null
        };
        db.ConfirmationReviewQuestions.Add(q);
        foreach (var (oc, ol) in options ?? Array.Empty<(string, string)>())
            db.ConfirmationReviewQuestionOptions.Add(new ConfirmationReviewQuestionOption { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationReviewQuestionId = q.Id, Code = oc, Label = ol });
        if (applicability is not null)
        {
            var set = new ConfirmationReviewQuestionApplicabilitySet { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationReviewQuestionId = q.Id };
            applicability(set);
            db.ConfirmationReviewQuestionApplicabilitySets.Add(set);
        }
        await db.SaveChangesAsync();
        return q.Id;
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

    private static async Task<Guid> AddHrbpUserAsync(HrmsDbContext db, Guid tenant, Guid? scopeDepartment = null)
    {
        if (!db.Roles.Any(r => r.Name == RoleNames.HRBP))
            db.Roles.Add(new Role { Id = 10, Name = RoleNames.HRBP });
        var userId = Guid.NewGuid();
        var hrbpEmployee = Guid.NewGuid();
        var linkId = Guid.NewGuid();
        db.Users.Add(new User { Id = userId, TenantId = tenant, Email = Guid.NewGuid() + "@hrbp.local", FirstName = "H", LastName = "RBP", PasswordHash = "x" });
        db.Employees.Add(new Employee { Id = hrbpEmployee, TenantId = tenant, FirstName = "Hr", LastName = "Bp", Email = Guid.NewGuid() + "@e.local", DateOfJoining = Doj, Gender = Gender.Unspecified });
        // The current link requires its creation event (composite FK TenantId+SubjectUserId+Id == TenantId+UserId+LinkId).
        db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
        {
            Id = linkId, TenantId = tenant, SubjectUserId = userId, ActorUserId = userId, Sequence = 1,
            Operation = "Link", NewLinkId = linkId, AfterEmployeeId = hrbpEmployee,
            OccurredAtUtc = DateTime.UtcNow, Reason = "seed", CorrelationId = "seed"
        });
        db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenant, UserId = userId, EmployeeId = hrbpEmployee });
        var assignment = new UserRole { Id = Guid.NewGuid(), TenantId = tenant, UserId = userId, RoleId = 10, EffectiveFrom = new(2026, 1, 1) };
        if (scopeDepartment is Guid dept)
            assignment.Scopes.Add(new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = tenant, ScopeType = RoleScopeType.Department, ScopeEntityId = dept });
        db.UserRoles.Add(assignment);
        await db.SaveChangesAsync();
        return hrbpEmployee;
    }

    // ── Test doubles ─────────────────────────────────────────────────────────────

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
        /// <summary>When set, the case's resolved manager; must be a seeded employee (Case has an FK to it).</summary>
        public Guid? ManagerId { get; set; }
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken ct = default)
            => Task.FromResult(Result<EmployeeManagerResolution>.Success(ManagerId is Guid m
                ? new(EmployeeManagerResolutionStatus.Resolved, employeeId, m, "MGR", "Manager", "resolved")
                : new(EmployeeManagerResolutionStatus.NoAssignedManager, employeeId, null, null, null, "no manager")));
        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class FakeHrbpResolver : IConfirmationHrbpResolver
    {
        public Task<ConfirmationHrbpResolutionResult> ResolveAsync(Guid tenantId, Guid employeeId, DateOnly effectiveDate, CancellationToken ct = default)
            => Task.FromResult(new ConfirmationHrbpResolutionResult(ConfirmationHrbpResolutionStatus.NoHrbp, null, "none"));
    }
}
