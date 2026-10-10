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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

/// <summary>
/// Provider regression coverage for Phase 9c-3.  Each scenario uses a unique tenant and is
/// rolled back where the service contract permits it. Provider wrappers own disposable databases;
/// this class never resolves application/default connection strings or performs provider setup.
/// </summary>
internal static class ConfirmationProviderRegressionAcceptance
{
    private static readonly DateOnly Doj = new(2026, 1, 15);
    private static readonly DateOnly ProbationEnd = new(2026, 7, 15);

    public static async Task RunAsync(Func<TestTenantContext, HrmsDbContext> createContext)
    {
        await using (var probe = createContext(new TestTenantContext()))
            AssertProviderAndMigrationConfiguration(probe);
        await OutcomesAndEmployeeSynchronizationAsync(createContext);
        await ApprovalModesAsync(createContext);
        await DashboardScopeAsync(createContext);
        await TenantAndEmployeeAccessAsync(createContext);
        await ConcurrencyAndRollbackAsync(createContext);
    }

    private static async Task OutcomesAndEmployeeSynchronizationAsync(Func<TestTenantContext, HrmsDbContext> createContext)
    {
        await InTransactionAsync(createContext, async (db, tenant) =>
        {
            foreach (var (rating, expected, expectedStatus) in new[]
            {
                (5, ConfirmationOutcome.Confirmed, "Confirmed"),
                (3, ConfirmationOutcome.ProbationExtended, (string?)null),
                (1, ConfirmationOutcome.NotConfirmed, "Not Confirmed"),
                (5, ConfirmationOutcome.AdditionalReview, (string?)null)
            })
            {
                var handle = await SeedCaseAsync(db, tenant, ConfirmationApprovalRequirement.ManagerAndHrbp, ApprovalSequence.Parallel, maxExtensions: 2, decisionOutcome: expected);
                await SubmitReviewsAsync(handle, rating);
                var result = await handle.Decision.SubmitDecisionAsync(handle.CaseId, new(expected));
                Assert.Equal(ResultStatus.Success, result.Status);
                Assert.Equal(expected, result.Value!.Outcome);
                var persisted = await db.ConfirmationReviewCases.AsNoTracking().SingleAsync(x => x.Id == handle.CaseId);
                Assert.Equal(expected == ConfirmationOutcome.AdditionalReview ? ConfirmationReviewCaseStatus.InReview : ConfirmationReviewCaseStatus.Decided, persisted.Status);
                var submittedRatings = await db.ConfirmationReviewResponses
                    .AsNoTracking()
                    .Where(x => x.ConfirmationReviewerReview!.ConfirmationReviewCaseId == handle.CaseId
                        && x.ConfirmationReviewerReview.Status == ConfirmationReviewStatus.Submitted
                        && x.ConfirmationReviewCaseQuestion!.QuestionType == ReviewQuestionType.Rating)
                    .Select(x => new { x.ConfirmationReviewerReviewId, x.RatingValue })
                    .ToListAsync();
                Assert.Equal(2, submittedRatings.Count);
                Assert.All(submittedRatings, response => Assert.Equal((int?)rating, response.RatingValue));

                var configuredRatingMethod = await db.ConfirmationReviewCases
                    .Where(x => x.Id == handle.CaseId)
                    .Join(db.ConfirmationPolicyRules,
                        reviewCase => reviewCase.ConfirmationPolicyVersionId,
                        policyRule => policyRule.ConfirmationPolicyVersionId,
                        (_, policyRule) => policyRule.OverallRatingMethod)
                    .SingleAsync();
                var expectedOverallRating = configuredRatingMethod switch
                {
                    OverallRatingMethod.AverageOfRatings => submittedRatings.Average(x => (decimal)x.RatingValue!.Value),
                    OverallRatingMethod.MinimumRating => submittedRatings.Min(x => (decimal)x.RatingValue!.Value),
                    _ => throw new XunitException($"Unhandled overall rating method: {configuredRatingMethod}")
                };
                var persistedDecision = await db.ConfirmationDecisions.AsNoTracking().SingleAsync(x => x.ConfirmationReviewCaseId == handle.CaseId);
                Assert.Equal(expected, persistedDecision.Outcome);
                Assert.Equal((decimal?)expectedOverallRating, persistedDecision.CalculatedOverallRating);
                Assert.NotEmpty(await db.ConfirmationReviewCaseEvents.Where(x => x.ConfirmationReviewCaseId == handle.CaseId && x.EventType == ConfirmationReviewCaseEventType.DecisionSubmitted).ToListAsync());
                var employment = await db.EmployeeEmployments.AsNoTracking().SingleAsync(x => x.EmployeeId == handle.Employee);
                var employee = await db.Employees.AsNoTracking().SingleAsync(x => x.Id == handle.Employee);
                Assert.Equal(expectedStatus, employment.JobStatus);
                Assert.Equal(Doj, employee.DateOfJoining);
                if (expected == ConfirmationOutcome.Confirmed)
                    Assert.Equal(ProbationEnd, employment.ConfirmationDate);
                if (expected == ConfirmationOutcome.NotConfirmed)
                    Assert.Null(employment.ConfirmationDate);
                if (expected == ConfirmationOutcome.ProbationExtended)
                {
                    var extension = await db.ConfirmationProbationExtensions.AsNoTracking().SingleAsync(x => x.ConfirmationReviewCaseId == handle.CaseId);
                    Assert.Equal(ProbationEnd, extension.PreviousEndDate);
                    Assert.Equal(ProbationEnd.AddMonths(3), extension.NewEndDate);
                    Assert.Equal(2, await db.ConfirmationProbationCycles.CountAsync(x => x.EmployeeId == handle.Employee));
                }
                if (expected == ConfirmationOutcome.AdditionalReview)
                {
                    Assert.Equal(2, persisted.ReviewRound);
                    Assert.Equal(2, (await db.ConfirmationDecisions.SingleAsync(x => x.ConfirmationReviewCaseId == handle.CaseId)).ReviewRound);
                    Assert.Contains(await db.ConfirmationReviewCaseEvents.Where(x => x.ConfirmationReviewCaseId == handle.CaseId).Select(x => x.EventType).ToListAsync(), x => x == ConfirmationReviewCaseEventType.AdditionalReviewRequested);
                }
            }
        });
    }

    private static async Task ApprovalModesAsync(Func<TestTenantContext, HrmsDbContext> createContext)
    {
        await InTransactionAsync(createContext, async (db, tenant) =>
        {
            var managerOnly = await SeedCaseAsync(db, tenant, ConfirmationApprovalRequirement.Manager);
            Assert.Equal(ResultStatus.Success, (await managerOnly.Cases.SubmitReviewAsync(managerOnly.CaseId, ConfirmationReviewerType.ReportingManager, Review(managerOnly, 5))).Status);
            Assert.Equal(ResultStatus.Success, (await managerOnly.Decision.SubmitDecisionAsync(managerOnly.CaseId, new(ConfirmationOutcome.Confirmed))).Status);

            var hrbpOnly = await SeedCaseAsync(db, tenant, ConfirmationApprovalRequirement.Hrbp);
            Assert.Equal(ResultStatus.Success, (await hrbpOnly.Cases.SubmitReviewAsync(hrbpOnly.CaseId, ConfirmationReviewerType.Hrbp, Review(hrbpOnly, 5))).Status);
            Assert.Equal(ResultStatus.Success, (await hrbpOnly.Decision.SubmitDecisionAsync(hrbpOnly.CaseId, new(ConfirmationOutcome.Confirmed))).Status);

            var either = await SeedCaseAsync(db, tenant, ConfirmationApprovalRequirement.ManagerOrHrbp);
            Assert.Equal(ResultStatus.Success, (await either.Cases.SubmitReviewAsync(either.CaseId, ConfirmationReviewerType.Hrbp, Review(either, 5))).Status);
            var eitherPreview = await either.Decision.CalculateAsync(either.CaseId);
            Assert.True(eitherPreview.Succeeded, eitherPreview.Message);
            Assert.True(eitherPreview.Value!.Reviewers.Single(x => x.ReviewerType == ConfirmationReviewerType.Hrbp).IsSubmitted);
            Assert.False(eitherPreview.Value.Reviewers.Single(x => x.ReviewerType == ConfirmationReviewerType.ReportingManager).IsRequired);
            Assert.Equal(ResultStatus.Success, (await either.Decision.SubmitDecisionAsync(either.CaseId, new(ConfirmationOutcome.Confirmed))).Status);

            var parallel = await SeedCaseAsync(db, tenant, ConfirmationApprovalRequirement.ManagerAndHrbp, ApprovalSequence.Parallel);
            await SubmitReviewsAsync(parallel, 5, hrbpFirst: true);
            Assert.Equal(ResultStatus.Success, (await parallel.Decision.SubmitDecisionAsync(parallel.CaseId, new(ConfirmationOutcome.Confirmed))).Status);

            var sequential = await SeedCaseAsync(db, tenant, ConfirmationApprovalRequirement.ManagerAndHrbp, ApprovalSequence.ManagerThenHrbp);
            var blocked = await sequential.Cases.SubmitReviewAsync(sequential.CaseId, ConfirmationReviewerType.Hrbp, Review(sequential, 5));
            Assert.Equal(ResultStatus.Conflict, blocked.Status);
            await SubmitReviewsAsync(sequential, 5);
            Assert.Equal(ResultStatus.Success, (await sequential.Decision.SubmitDecisionAsync(sequential.CaseId, new(ConfirmationOutcome.Confirmed))).Status);
        });
    }

    private static async Task DashboardScopeAsync(Func<TestTenantContext, HrmsDbContext> createContext)
    {
        await InTransactionAsync(createContext, async (db, tenant) =>
        {
            var employeeA = await AddEmployeeAsync(db, tenant);
            var employeeB = await AddEmployeeAsync(db, tenant);
            await AddDashboardCaseAsync(db, tenant, employeeA, ConfirmationApprovalRequirement.Manager);
            await AddDashboardCaseAsync(db, tenant, employeeB, ConfirmationApprovalRequirement.Manager);
            var access = new AllowListScope(employeeA);
            var dashboard = new HrConfirmationDashboardService(db, new TestTenantContext(tenant), access);
            var result = await dashboard.GetSummaryAsync(new HrConfirmationDashboardFilter());
            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(1, result.Value!.Kpis.ProbationEmployees);
            Assert.Single(result.Value.PendingActions);
            Assert.Equal(employeeA.ToString("N")[..8], result.Value.PendingActions.Single().EmployeeCode);
        });
    }

    private static async Task TenantAndEmployeeAccessAsync(Func<TestTenantContext, HrmsDbContext> createContext)
    {
        await InTransactionAsync(createContext, async (db, tenant) =>
        {
            var handle = await SeedCaseAsync(db, tenant, ConfirmationApprovalRequirement.Manager);
            Assert.Equal(ResultStatus.Success, (await handle.Cases.SubmitReviewAsync(handle.CaseId, ConfirmationReviewerType.ReportingManager, Review(handle, 5))).Status);
            handle.Access.Allow = false;
            Assert.Equal(ResultStatus.Forbidden, (await handle.Cases.GetCaseAsync(handle.CaseId)).Status);
            Assert.Equal(ResultStatus.Forbidden, (await handle.Cases.GetHistoryAsync(handle.CaseId)).Status);
            Assert.Equal(ResultStatus.Forbidden, (await handle.Decision.SubmitDecisionAsync(handle.CaseId, new(ConfirmationOutcome.Confirmed))).Status);
            Assert.Equal(ResultStatus.Forbidden, (await handle.Decision.GetHistoryAsync(handle.CaseId)).Status);
            Assert.Equal(ResultStatus.Forbidden, (await new HrConfirmationDashboardService(db, new TestTenantContext(tenant), handle.Access).GetSummaryAsync(new HrConfirmationDashboardFilter())).Status);

            var otherTenant = Guid.NewGuid();
            await AddTenantAsync(db, otherTenant);
            var otherAccess = new AllowListScope(handle.Employee);
            var other = BuildServices(db, otherTenant, otherAccess);
            Assert.Equal(ResultStatus.NotFound, (await other.Cases.GetCaseAsync(handle.CaseId)).Status);
            Assert.Equal(ResultStatus.NotFound, (await other.Cases.SubmitReviewAsync(handle.CaseId, ConfirmationReviewerType.ReportingManager, Review(handle, 5))).Status);
            Assert.Equal(ResultStatus.NotFound, (await other.Decision.CalculateAsync(handle.CaseId)).Status);
            Assert.Equal(ResultStatus.NotFound, (await other.Decision.GetHistoryAsync(handle.CaseId)).Status);
            Assert.Equal(ResultStatus.Success, (await new HrConfirmationDashboardService(db, new TestTenantContext(otherTenant), otherAccess).GetSummaryAsync(new HrConfirmationDashboardFilter())).Status);
        });
    }

    private static async Task ConcurrencyAndRollbackAsync(Func<TestTenantContext, HrmsDbContext> createContext)
    {
        var tenant = Guid.NewGuid();
        Guid caseId;
        Guid employeeId;
        await using (var seed = createContext(new TestTenantContext(tenant)))
        {
            await AddTenantAsync(seed, tenant);
            var handle = await SeedCaseAsync(seed, tenant, ConfirmationApprovalRequirement.Manager);
            Assert.Equal(ResultStatus.Success, (await handle.Cases.SubmitReviewAsync(handle.CaseId, ConfirmationReviewerType.ReportingManager, Review(handle, 5))).Status);
            await seed.SaveChangesAsync();
            caseId = handle.CaseId;
            employeeId = handle.Employee;
        }
        await using (var first = createContext(new TestTenantContext(tenant, Guid.NewGuid())))
        await using (var second = createContext(new TestTenantContext(tenant, Guid.NewGuid())))
        {
            var a = BuildServices(first, tenant, new AllowListScope(employeeId)).Decision;
            var b = BuildServices(second, tenant, new AllowListScope(employeeId)).Decision;
            var results = await Task.WhenAll(a.SubmitDecisionAsync(caseId, new(ConfirmationOutcome.Confirmed)), b.SubmitDecisionAsync(caseId, new(ConfirmationOutcome.Confirmed)));
            Assert.Equal(1, results.Count(x => x.Status == ResultStatus.Success));
            Assert.Equal(1, results.Count(x => x.Status == ResultStatus.Conflict));
        }
        await using (var verify = createContext(new TestTenantContext(tenant)))
        {
            var finalCase = await verify.ConfirmationReviewCases.AsNoTracking().SingleAsync(x => x.Id == caseId);
            Assert.Equal(ConfirmationReviewCaseStatus.Decided, finalCase.Status);
            Assert.Equal(ConfirmationOutcome.Confirmed, finalCase.Outcome);
            Assert.Single(await verify.ConfirmationDecisions.AsNoTracking().Where(x => x.ConfirmationReviewCaseId == caseId).ToListAsync());
        }

        await InTransactionAsync(createContext, async (db, tenant) =>
        {
            db.Employees.Add(new Employee { Id = Guid.NewGuid(), TenantId = tenant, FirstName = "Rollback", LastName = "Probe", Email = $"{Guid.NewGuid():N}@test.invalid", DateOfJoining = Doj });
            await db.SaveChangesAsync();
            throw new RollbackProbeException();
        }, expectRollback: true);
    }

    private static async Task InTransactionAsync(Func<TestTenantContext, HrmsDbContext> createContext, Func<HrmsDbContext, Guid, Task> action, bool expectRollback = false)
    {
        var tenant = Guid.NewGuid();
        await using var db = createContext(new TestTenantContext(tenant));
        await using var transaction = await db.Database.BeginTransactionAsync();
        await AddTenantAsync(db, tenant);
        try
        {
            await action(db, tenant);
            await transaction.RollbackAsync();
        }
        catch (RollbackProbeException) when (expectRollback)
        {
            await transaction.RollbackAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
        finally
        {
            Assert.False(await db.Employees.AnyAsync(x => x.FirstName == "Rollback"));
        }
    }

    private static async Task<Handle> SeedCaseAsync(HrmsDbContext db, Guid tenant, ConfirmationApprovalRequirement requirement, ApprovalSequence sequence = ApprovalSequence.Parallel, int maxExtensions = 0, ConfirmationOutcome? decisionOutcome = null)
    {
        var employee = await AddEmployeeAsync(db, tenant);
        var policy = new ConfirmationPolicy { Id = Guid.NewGuid(), TenantId = tenant, Code = Guid.NewGuid().ToString("N")[..8], Name = "Provider regression", IsActive = true };
        var version = new ConfirmationPolicyVersion { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyId = policy.Id, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = ConfirmationPolicyVersionStatus.Published };
        db.ConfirmationPolicies.Add(policy);
        db.ConfirmationPolicyVersions.Add(version);
        db.ConfirmationPolicyRules.Add(new ConfirmationPolicyRule { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id, ProbationDuration = 6, ProbationDurationUnit = ProbationDurationUnit.Months, RequiredApproval = requirement, ApprovalSequence = sequence, MaxExtensions = maxExtensions, ExtensionDuration = 3, ExtensionDurationUnit = ProbationDurationUnit.Months, OverallRatingMethod = OverallRatingMethod.AverageOfRatings });
        var rules = decisionOutcome switch
        {
            ConfirmationOutcome.ProbationExtended => new[] { DecisionRule(tenant, version.Id, 1, "Extend", 3, 3, ConfirmationOutcome.ProbationExtended) },
            ConfirmationOutcome.NotConfirmed => new[] { DecisionRule(tenant, version.Id, 1, "Reject", 1, 1, ConfirmationOutcome.NotConfirmed) },
            ConfirmationOutcome.AdditionalReview => new[] { DecisionRule(tenant, version.Id, 1, "Additional review", 5, 5, ConfirmationOutcome.AdditionalReview) },
            _ => new[] { DecisionRule(tenant, version.Id, 1, "Confirm", 5, 5, ConfirmationOutcome.Confirmed) }
        };
        db.ConfirmationDecisionRules.AddRange(rules);
        var scale = new ConfirmationRatingScale { Id = Guid.NewGuid(), TenantId = tenant, Code = "P5", Name = "Five", MinRating = 1, MaxRating = 5, IsActive = true };
        db.ConfirmationRatingScales.Add(scale);
        db.ConfirmationRatingScaleValues.AddRange(Enumerable.Range(1, 5).Select(i => new ConfirmationRatingScaleValue { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationRatingScaleId = scale.Id, RatingValue = i, Label = $"L{i}", DisplayOrder = i, IsActive = true }));
        db.ConfirmationReviewQuestions.Add(new ConfirmationReviewQuestion { Id = Guid.NewGuid(), TenantId = tenant, Code = "OVERALL", QuestionText = "Overall", QuestionType = ReviewQuestionType.Rating, IsMandatory = true, IsActive = true, DisplayOrder = 1, ConfirmationRatingScaleId = scale.Id });
        await db.SaveChangesAsync();
        var access = new AllowListScope(employee);
        var services = BuildServices(db, tenant, access);
        var created = await services.Cases.CreateCaseAsync(new(employee, new(2026, 6, 1)));
        Assert.Equal(ResultStatus.Success, created.Status);
        return new Handle(services.Decision, services.Cases, created.Value!.Case.Id, tenant, employee, access, db);
    }

    private static async Task SubmitReviewsAsync(Handle handle, int rating, bool hrbpFirst = false)
    {
        if (hrbpFirst) AssertReview(await handle.Cases.SubmitReviewAsync(handle.CaseId, ConfirmationReviewerType.Hrbp, Review(handle, rating)));
        AssertReview(await handle.Cases.SubmitReviewAsync(handle.CaseId, ConfirmationReviewerType.ReportingManager, Review(handle, rating)));
        if (!hrbpFirst) AssertReview(await handle.Cases.SubmitReviewAsync(handle.CaseId, ConfirmationReviewerType.Hrbp, Review(handle, rating)));
    }

    private static void AssertReview<T>(Result<T> result) => Assert.Equal(ResultStatus.Success, result.Status);
    private static ConfirmationDecisionRule DecisionRule(Guid tenant, Guid version, int priority, string name, decimal min, decimal max, ConfirmationOutcome outcome) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version, Priority = priority, Name = name,
        MinOverallRating = min, MaxOverallRating = max, Outcome = outcome, IsActive = true
    };

    private static SubmitReviewerReviewRequest Review(Handle handle, int rating) => new(null, new[] { new ConfirmationReviewResponseInput(RatingQuestionId(handle), rating) });
    private static Guid RatingQuestionId(Handle handle) => handle.Context.ConfirmationReviewCaseQuestions.AsNoTracking().Single(x => x.ConfirmationReviewCaseId == handle.CaseId && x.QuestionType == ReviewQuestionType.Rating).Id;

    private static async Task<Guid> AddEmployeeAsync(HrmsDbContext db, Guid tenant)
    {
        var id = Guid.NewGuid();
        db.Employees.Add(new Employee { Id = id, TenantId = tenant, EmployeeCode = id.ToString("N")[..8], FirstName = "Provider", LastName = "Employee", Email = $"{id:N}@test.invalid", DateOfJoining = Doj, Status = EmployeeStatus.Active });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = id, EffectiveFrom = Doj });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task AddTenantAsync(HrmsDbContext db, Guid tenant)
    {
        db.Tenants.Add(new Tenant { Id = tenant, TenantCode = tenant.ToString("N")[..8], TenantName = "Provider test", Host = $"{tenant:N}.test.invalid", ShardKey = tenant.ToString("N"), Status = TenantStatus.Active });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> AddDashboardCaseAsync(HrmsDbContext db, Guid tenant, Guid employee, ConfirmationApprovalRequirement requirement)
    {
        var policy = new ConfirmationPolicy { Id = Guid.NewGuid(), TenantId = tenant, Code = Guid.NewGuid().ToString("N")[..8], Name = "Dashboard", IsActive = true };
        var version = new ConfirmationPolicyVersion { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyId = policy.Id, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = ConfirmationPolicyVersionStatus.Published };
        var id = Guid.NewGuid();
        db.AddRange(policy, version, new ConfirmationPolicyRule { Id = Guid.NewGuid(), TenantId = tenant, ConfirmationPolicyVersionId = version.Id, RequiredApproval = requirement, ApprovalSequence = ApprovalSequence.Parallel, ProbationDuration = 6 }, new ConfirmationReviewCase { Id = id, TenantId = tenant, EmployeeId = employee, ConfirmationPolicyId = policy.Id, ConfirmationPolicyVersionId = version.Id, ProbationCycleKey = id.ToString("N"), ReviewRound = 1, CycleNumber = 1, ProbationStartDate = Doj, ProbationEndDate = ProbationEnd, DueDate = ProbationEnd, Status = ConfirmationReviewCaseStatus.InReview });
        await db.SaveChangesAsync();
        return id;
    }

    private static (IConfirmationDecisionService Decision, IConfirmationReviewCaseService Cases) BuildServices(HrmsDbContext db, Guid tenant, AllowListScope access)
    {
        var tc = new TestTenantContext(tenant, Guid.NewGuid());
        var cases = new ConfirmationReviewCaseService(db, tc, new ConfirmationPolicyResolver(db, tenantContext: tc), new ConfirmationReviewQuestionResolver(db, tenantContext: tc), new NoHrbpResolver(), new NoManagerResolver(), access);
        return (new ConfirmationDecisionService(db, tc, access, cases, new JobStatusService(db, tc, new DecideAuthorization(), TimeProvider.System)), cases);
    }

    private static void AssertProviderAndMigrationConfiguration(HrmsDbContext db)
    {
        var provider = db.Database.ProviderName ?? string.Empty;
        Assert.True(provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) || provider.Contains("MySql", StringComparison.OrdinalIgnoreCase), $"Unexpected provider: {provider}");
        var migrationsAssembly = db.GetService<IMigrationsAssembly>().Assembly.GetName().Name ?? string.Empty;
        Assert.False(string.IsNullOrWhiteSpace(migrationsAssembly), "The provider context has no migrations assembly.");
        if (provider.Contains("MySql", StringComparison.OrdinalIgnoreCase))
            Assert.Equal("HRMS.Infrastructure.MySqlMigrations", migrationsAssembly);
        else
            Assert.Equal(typeof(HrmsDbContext).Assembly.GetName().Name, migrationsAssembly);
    }

    private sealed record Handle(IConfirmationDecisionService Decision, IConfirmationReviewCaseService Cases, Guid CaseId, Guid Tenant, Guid Employee, AllowListScope Access, HrmsDbContext Context);
    private sealed class RollbackProbeException : Exception;
    private sealed class DecideAuthorization : ICurrentAuthorizationContext { public bool HasAnyPermission(params string[] requested) => requested.Contains(Permissions.Confirmation.Decide); }
    private sealed class NoManagerResolver : IEmployeeManagerResolver { public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid e, DateOnly d, CancellationToken c = default) => Task.FromResult(Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.NoAssignedManager, e, null, null, null, "none"))); public Task<bool> WouldCreateCycleAsync(Guid e, Guid m, DateOnly d, CancellationToken c = default) => Task.FromResult(false); }
    private sealed class NoHrbpResolver : IConfirmationHrbpResolver { public Task<ConfirmationHrbpResolutionResult> ResolveAsync(Guid t, Guid e, DateOnly d, CancellationToken c = default) => Task.FromResult(new ConfirmationHrbpResolutionResult(ConfirmationHrbpResolutionStatus.NoHrbp, null, "none")); }
    private sealed class AllowListScope(Guid employee) : IEmployeeAccessScopeService { public bool Allow { get; set; } = true; public Task<Expression<Func<Employee, bool>>> BuildPredicateAsync(DateOnly d, CancellationToken c = default) => Task.FromResult<Expression<Func<Employee, bool>>>(x => Allow && x.Id == employee); public Task<Expression<Func<Employee, bool>>> BuildRoleScopePredicateAsync(DateOnly d, CancellationToken c = default) => BuildPredicateAsync(d, c); public Task<bool> CanAccessEmployeeAsync(Guid id, DateOnly d, CancellationToken c = default) => Task.FromResult(Allow && id == employee); }
}

public sealed class SqlServerConfirmationProviderRegressionTests
{
    [SqlServerIntegrationFact, Trait("Category", "SqlServerIntegration")]
    public async Task Confirmation_regressions_run_against_sql_server()
    {
        var fixture = new SqlServerIntegrationTestHarness();
        await using var disposable = await fixture.CreateDisposableDatabaseAsync("HRMS_Phase9c3_Confirmation_Test_");
        Assert.NotEqual(fixture.DatabaseName, disposable.DatabaseName);
        SqlServerIntegrationTestHarness.ValidateIntegrationDatabase(disposable.DatabaseName);
        await ConfirmationProviderRegressionAcceptance.RunAsync(t => disposable.CreateContext(t));
    }
}

[Collection("MySQL Integration")]
public sealed class MySqlConfirmationProviderRegressionTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task Confirmation_regressions_run_against_mysql()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL confirmation provider tests require HRMS_MYSQL_TEST_CONNECTION; no default or production connection is used.");
        var source = new MySqlConnectionStringBuilder(MySqlApiFactory.NormalizeConnectionString(configured));
        var sourceDatabase = source.Database;
        var name = $"HRMS_Phase9c3_Confirmation_Test_{Guid.NewGuid():N}";
        ValidateMySqlDisposableName(name, sourceDatabase);
        var admin = new MySqlConnectionStringBuilder(source.ConnectionString) { Database = string.Empty };
        var target = new MySqlConnectionStringBuilder(source.ConnectionString) { Database = name };
        Exception? testFailure = null;
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString))
            {
                await server.OpenAsync();
                await using var create = server.CreateCommand();
                create.CommandText = $"CREATE DATABASE `{name}`";
                await create.ExecuteNonQueryAsync();
            }
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>()
                .UseMySQL(target.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations"))
                .Options, new TestTenantContext());
            await db.Database.MigrateAsync();
            await ConfirmationProviderRegressionAcceptance.RunAsync(t => new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>()
                .UseMySQL(target.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations"))
                .Options, t));
        }
        catch (Exception ex)
        {
            testFailure = ex;
            throw;
        }
        finally
        {
            try
            {
                ValidateMySqlDisposableName(name, sourceDatabase);
                await using var server = new MySqlConnection(admin.ConnectionString);
                await server.OpenAsync();
                await using (var drop = server.CreateCommand())
                {
                    drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`";
                    await drop.ExecuteNonQueryAsync();
                }
                await using var verify = server.CreateCommand();
                verify.CommandText = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.SCHEMATA WHERE SCHEMA_NAME = @name";
                verify.Parameters.AddWithValue("@name", name);
                Assert.Equal(0L, Convert.ToInt64(await verify.ExecuteScalarAsync()));
            }
            catch (Exception cleanupFailure)
            {
                var message = $"MySQL disposable database cleanup failed for '{name}'; source database '{sourceDatabase}' was not targeted. {cleanupFailure.Message}";
                if (testFailure is null) throw new XunitException(message);
                throw new AggregateException(message, testFailure, cleanupFailure);
            }
        }
    }

    private static void ValidateMySqlDisposableName(string name, string? sourceDatabase)
    {
        var forbidden = new[] { "", "information_schema", "mysql", "performance_schema", "sys", "test", "hrms", "production", "prod" };
        Assert.Matches("^HRMS_Phase9c3_Confirmation_Test_[0-9a-f]{32}$", name);
        Assert.DoesNotContain(name, forbidden, StringComparer.OrdinalIgnoreCase);
        Assert.False(string.Equals(name, sourceDatabase, StringComparison.OrdinalIgnoreCase), "The disposable MySQL database must never equal the source database.");
    }
}
