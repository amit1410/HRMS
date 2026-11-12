using HRMS.Application.Abstractions;
using HRMS.Application.DTOs.Employees;
using HRMS.Application.EmployeeCodes;
using HRMS.Application.Services;
using HRMS.Application.Validators.Employees;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;

namespace HRMS.Tests;

public sealed class EmployeeOnboardingOrchestrationTests
{
    private static readonly Guid Tenant = SeedData.TenantIds.Demo01;
    private static readonly DateOnly Today = new(2026, 3, 4);

    [Fact]
    public async Task Complete_onboarding_persists_all_rows_audits_and_simple_employee_code_for_tenant()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync();

        var result = await f.Service.CreateOnboardingAsync(Request(), "qa-user");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("EMP-00001", result.Value!.EmployeeCode);
        Assert.Equal("Asha", result.Value.FirstName);
        Assert.Equal("On Probation", result.Value.JobStatus);
        var employeeId = result.Value.Id;
        using var verify = f.Database.CreateContext(new TestTenantContext());
        var employee = await verify.Employees.IgnoreQueryFilters().SingleAsync(x => x.Id == employeeId);
        var contact = await verify.EmployeeContacts.IgnoreQueryFilters().SingleAsync(x => x.EmployeeId == employeeId);
        var history = await verify.EmployeeEmploymentHistory.IgnoreQueryFilters().SingleAsync(x => x.EmployeeId == employeeId);
        var audits = await verify.EmployeeAuditLogs.IgnoreQueryFilters().Where(x => x.EmployeeId == employeeId).ToListAsync();
        Assert.Equal(Tenant, employee.TenantId);
        Assert.Equal(Tenant, contact.TenantId);
        Assert.Equal(Tenant, history.TenantId);
        Assert.Equal("Asha", employee.FirstName);
        Assert.Equal("Rao", employee.LastName);
        Assert.Equal(Today, employee.DateOfJoining);
        Assert.Equal("999988887777", employee.AadhaarNumber);
        Assert.Equal("ABCDE1234F", employee.PanNumber);
        Assert.Equal("asha.work@example.test", contact.OfficialEmail);
        Assert.Equal("9998887776", contact.OfficialPhone);
        Assert.Equal(Today, history.EffectiveFrom);
        Assert.Equal("qa-user", history.CreatedBy);
        Assert.Equal("On Probation", employee.JobStatus);
        Assert.Contains(audits, x => x.FieldName == "JobStatus" && x.NewValue == "On Probation");
        Assert.Contains(audits, x => x.TenantId == Tenant);
    }

    [Fact]
    public async Task Caller_owned_import_transaction_commits_onboarding_row_and_audit_association_together()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync();
        f.Tenant.UserId = SeedData.Users.First(x => x.TenantId == Tenant).Id;
        var persistence = new EmployeeImportPersistenceService(f.Db, f.Tenant, TimeProvider.System);
        var preparationLease = Guid.NewGuid();
        var operationResult = await persistence.BeginPreparationAsync(
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 1, preparationLease);
        Assert.True(operationResult.Succeeded, operationResult.Message);
        Assert.True((await persistence.CompletePreparationAsync(operationResult.Value!.Id, preparationLease)).Succeeded);
        var operationId = operationResult.Value!.Id;
        var leaseId = Guid.NewGuid();
        Assert.True((await persistence.ClaimAsync(operationId, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", leaseId)).Succeeded);
        Assert.True((await persistence.TransitionAsync(operationId, EmployeeImportOperationState.Processing)).Succeeded);
        Assert.True((await persistence.StartRowAsync(operationId, 2)).Succeeded);

        await using var transaction = await f.Db.Database.BeginTransactionAsync();
        var employee = await f.Service.CreateOnboardingWithinTransactionAsync(Request(), "qa-user", operationId, transaction);
        Assert.True(employee.Succeeded, employee.Message);
        Assert.NotNull(f.Db.Database.CurrentTransaction);
        var row = await persistence.CompleteRowWithinTransactionAsync(operationId, 2, employee.Value!.Id, employee.Value.EmployeeCode, leaseId, transaction);
        Assert.True(row.Succeeded, row.Message);
        Assert.Equal(EmployeeImportRowStatus.Succeeded, row.Value!.Status);
        await transaction.CommitAsync();

        using var verify = f.Database.CreateContext(new TestTenantContext());
        Assert.True(await verify.Employees.IgnoreQueryFilters().AnyAsync(x => x.Id == employee.Value.Id && x.TenantId == Tenant));
        Assert.True(await verify.EmployeeImportRows.IgnoreQueryFilters().AnyAsync(x => x.Id == row.Value.Id && x.Status == EmployeeImportRowStatus.Succeeded));
        Assert.Contains(await verify.EmployeeAuditLogs.IgnoreQueryFilters().Where(x => x.EmployeeId == employee.Value.Id).ToListAsync(), x => x.ImportBatchId == operationId);
    }

    [Fact]
    public async Task Onboarding_uses_rule_based_code_generation()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync(EmployeeCodeGenerationMethod.RuleBased);
        var configured = await f.Configuration.SaveRuleAsync(null, new EmployeeCodeRuleRequest
        {
            Name = "Engineering onboarding", Priority = 1, Status = EmployeeCodeRuleStatus.Active,
            Conditions = [new() { Field = EmployeeCodeConditionField.Department, ReferenceId = OrganizationTestHarness.DepartmentId(Tenant, "ENG") }],
            Segments =
            [
                new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.HoldingCompanyCode },
                new() { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.DepartmentCode },
                new() { SequenceOrder = 3, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 4 }
            ]
        });
        Assert.True(configured.Succeeded, configured.Message);

        var result = await f.Service.CreateOnboardingAsync(Request(), "qa-user");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("HC01-ENG-0001", result.Value!.EmployeeCode);
    }

    [Fact]
    public async Task Manual_code_configuration_requires_and_persists_requested_code()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync(EmployeeCodeGenerationMethod.Simple, EmployeeCodeAssignmentMode.Manual);
        var request = Request();
        request.InitialEmployment.EmployeeCode = "MAN-2026-17";

        var result = await f.Service.CreateOnboardingAsync(request, "qa-user");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("MAN-2026-17", result.Value!.EmployeeCode);
    }

    [Theory]
    [InlineData(2018, 1, 20)]
    [InlineData(2027, 1, 20)]
    public async Task Initial_onboarding_preserves_historical_and_future_employment_dates(int year, int month, int day)
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync(EmployeeCodeGenerationMethod.Simple, EmployeeCodeAssignmentMode.Manual,
            new DateOnly(2017, 1, 2), new DateOnly(2031, 9, 30));
        var effectiveDate = new DateOnly(year, month, day);
        var request = Request();
        request.PersonalDetails.DateOfJoining = effectiveDate;
        request.InitialEmployment.EffectiveFrom = effectiveDate;
        request.InitialEmployment.EmployeeCode = "EMP000001";

        var result = await f.Service.CreateOnboardingAsync(request, "qa-user");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("EMP000001", result.Value!.EmployeeCode);
        using var verify = f.Database.CreateContext(new TestTenantContext());
        var employee = await verify.Employees.IgnoreQueryFilters().SingleAsync(x => x.Id == result.Value.Id);
        var history = await verify.EmployeeEmploymentHistory.IgnoreQueryFilters().SingleAsync(x => x.EmployeeId == result.Value.Id);
        Assert.Equal(effectiveDate, employee.DateOfJoining);
        Assert.Equal(effectiveDate, history.EffectiveFrom);
    }

    [Fact]
    public async Task Initial_onboarding_rejects_mismatched_joining_and_employment_dates()
    {
        using var f = await Fixture.CreateAsync();
        var request = Request();
        request.InitialEmployment.EffectiveFrom = Today.AddDays(-1);

        var result = await f.Service.CreateOnboardingAsync(request, "qa-user");

        Assert.False(result.Succeeded);
        Assert.Equal(HRMS.Application.Common.ResultStatus.ValidationFailed, result.Status);
        using var verify = f.Database.CreateContext(new TestTenantContext());
        Assert.False(await verify.Employees.IgnoreQueryFilters().AnyAsync(x => x.TenantId == Tenant && x.Email == "asha.work@example.test"));
    }

    [Theory]
    [InlineData("employee-create")]
    [InlineData("contact")]
    [InlineData("initial-history")]
    [InlineData("history-write")]
    [InlineData("employee-code")]
    [InlineData("job-status")]
    [InlineData("audit")]
    public async Task Failed_onboarding_leaves_no_partial_records(string failure)
    {
        using var f = await Fixture.CreateAsync(new OnboardingFailureInterceptor(failure));
        if (failure is not "employee-code" and not "initial-history")
            await f.ConfigureCodeAsync();
        var request = Request();
        if (failure == "initial-history") request.InitialEmployment.ChangeReason = EmploymentChangeReason.Promotion;
        if (failure == "employee-code") request.InitialEmployment.EmployeeCode = null;
        if (failure == "job-status") f.Authorization.SetPermissions(Permissions.Employee.Create);

        if (failure == "job-status") request.PersonalDetails.JobStatus = new string('X', 101);
        if (failure is "employee-create" or "contact" or "audit" or "history-write")
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CreateOnboardingAsync(request, "qa-user"));
        else
        {
            var result = await f.Service.CreateOnboardingAsync(request, "qa-user");
            Assert.False(result.Succeeded);
        }
        using var verify = f.Database.CreateContext(new TestTenantContext());
        Assert.Equal(0, await verify.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant && x.FirstName == "Asha"));
        Assert.Equal(0, await verify.EmployeeContacts.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant && x.OfficialEmail == "asha.work@example.test"));
        Assert.Equal(0, await (from h in verify.EmployeeEmploymentHistory.IgnoreQueryFilters()
                               join e in verify.Employees.IgnoreQueryFilters() on h.EmployeeId equals e.Id
                               where h.TenantId == Tenant && e.FirstName == "Asha" && h.EffectiveFrom == Today
                               select h).CountAsync());
        Assert.Equal(0, await verify.EmployeeAuditLogs.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant && x.NewValue == "On Probation"));
    }

    [Theory]
    [InlineData("permission")]
    [InlineData("jobStatus")]
    [InlineData("aadhaarNumber")]
    [InlineData("panNumber")]
    [InlineData("aadhaarFormat")]
    [InlineData("panFormat")]
    [InlineData("inactiveMaster")]
    [InlineData("foreignMaster")]
    [InlineData("manager")]
    [InlineData("inactiveManager")]
    [InlineData("crossTenantManager")]
    public async Task Invalid_or_unauthorized_onboarding_is_rejected_without_rows(string scenario)
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync();
        var request = Request();
        if (scenario == "permission") f.Authorization.SetPermissions();
        if (scenario == "jobStatus") request.PersonalDetails.JobStatus = null;
        if (scenario == "aadhaarNumber") request.PersonalDetails.AadhaarNumber = null;
        if (scenario == "panNumber") request.PersonalDetails.PanNumber = null;
        if (scenario == "aadhaarFormat") request.PersonalDetails.AadhaarNumber = "1234-invalid";
        if (scenario == "panFormat") request.PersonalDetails.PanNumber = "ABC123";
        if (scenario == "inactiveMaster")
        {
            using var edit = f.Database.CreateContext(f.Tenant);
            var department = await edit.Departments.SingleAsync(x => x.Id == request.InitialEmployment.DepartmentId);
            department.IsActive = false;
            await edit.SaveChangesAsync();
        }
        if (scenario == "foreignMaster") request.InitialEmployment.DepartmentId = OrganizationTestHarness.DepartmentId(SeedData.TenantIds.Demo02, "OPS");
        if (scenario == "manager") request.InitialEmployment.ManagerId = Guid.NewGuid();
        if (scenario == "inactiveManager")
        {
            var inactiveManager = Guid.NewGuid();
            f.Db.Employees.Add(new Employee
            {
                Id = inactiveManager, TenantId = Tenant, EmployeeCode = "INACTIVE-MGR",
                FirstName = "Inactive", LastName = "Manager", Email = "inactive.manager@example.test",
                DateOfJoining = Today, Status = EmployeeStatus.Resigned
            });
            f.Db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
            {
                Id = Guid.NewGuid(), TenantId = Tenant, EmployeeId = inactiveManager,
                EffectiveFrom = Today, EmploymentStatus = EmployeeStatus.Resigned
            });
            await f.Db.SaveChangesAsync();
            request.InitialEmployment.ManagerId = inactiveManager;
        }
        if (scenario == "crossTenantManager")
        {
            var otherTenantManager = Guid.NewGuid();
            using (var otherTenantContext = f.Database.CreateContext(new TestTenantContext(SeedData.TenantIds.Demo02)))
            {
                otherTenantContext.Employees.Add(new Employee
                {
                    Id = otherTenantManager, TenantId = SeedData.TenantIds.Demo02, EmployeeCode = "OTHER-MGR",
                    FirstName = "Other", LastName = "Manager", Email = "other.manager@example.test",
                    DateOfJoining = Today, Status = EmployeeStatus.Active
                });
                otherTenantContext.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
                {
                    Id = Guid.NewGuid(), TenantId = SeedData.TenantIds.Demo02, EmployeeId = otherTenantManager,
                    EffectiveFrom = Today, EmploymentStatus = EmployeeStatus.Active
                });
                await otherTenantContext.SaveChangesAsync();
            }
            Assert.Equal(Tenant, f.Tenant.TenantId);
            Assert.False(await f.Db.Employees.AnyAsync(x => x.Id == otherTenantManager));
            request.InitialEmployment.ManagerId = otherTenantManager;
        }

        var result = await f.Service.CreateOnboardingAsync(request, "qa-user");

        Assert.False(result.Succeeded, result.Message);
        using var verify = f.Database.CreateContext(new TestTenantContext());
        Assert.Equal(0, await verify.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant && x.FirstName == "Asha"));
        Assert.Equal(0, await (from h in verify.EmployeeEmploymentHistory.IgnoreQueryFilters()
                               join e in verify.Employees.IgnoreQueryFilters() on h.EmployeeId equals e.Id
                               where h.TenantId == Tenant && e.FirstName == "Asha" && h.EffectiveFrom == Today
                               select h).CountAsync());
    }

    [Fact]
    public async Task Missing_sensitive_edit_permission_does_not_persist_sensitive_values()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync();
        f.Authorization.SetPermissions(Permissions.Employee.Create);

        var result = await f.Service.CreateOnboardingAsync(Request(), "qa-user");

        Assert.True(result.Succeeded, result.Message);
        using var verify = f.Database.CreateContext(new TestTenantContext());
        var employee = await verify.Employees.IgnoreQueryFilters().SingleAsync(x => x.Id == result.Value!.Id);
        Assert.Null(employee.AadhaarNumber);
        Assert.Null(employee.PanNumber);
    }

    [Fact]
    public async Task Initial_history_requires_active_transaction_and_does_not_commit_callers_transaction()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync();
        var employee = await f.CreateDraftAsync();
        var noTransaction = await f.Employment.CreateInitialHistoryWithinTransactionAsync(employee, EmploymentRequest(), "qa-user");
        Assert.Equal(HRMS.Application.Common.ResultStatus.ServiceUnavailable, noTransaction.Status);

        await using var transaction = await f.Db.BeginTransactionAsync();
        var created = await f.Employment.CreateInitialHistoryWithinTransactionAsync(employee, EmploymentRequest(), "qa-user");
        Assert.True(created.Succeeded, created.Message);
        Assert.True(transaction.GetDbTransaction().Connection is not null);
        await transaction.RollbackAsync();
        using var verify = f.Database.CreateContext(new TestTenantContext());
        Assert.Equal(0, await verify.EmployeeEmploymentHistory.IgnoreQueryFilters().CountAsync(x => x.EmployeeId == employee));
        Assert.Null(await verify.Employees.IgnoreQueryFilters().Where(x => x.Id == employee).Select(x => x.EmployeeCode).SingleAsync());
    }

    [Fact]
    public async Task Complete_onboarding_opens_one_transaction_and_commits_once()
    {
        var tx = new TransactionCounter();
        using var f = await Fixture.CreateAsync(tx);
        await f.ConfigureCodeAsync();
        tx.Reset();

        var result = await f.Service.CreateOnboardingAsync(Request(), "qa-user");

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(1, tx.Begins);
        Assert.Equal(1, tx.Commits);
    }

    [Fact]
    public async Task Duplicate_manual_code_is_rejected_and_first_employee_is_preserved()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync(EmployeeCodeGenerationMethod.Simple, EmployeeCodeAssignmentMode.Manual);
        var firstRequest = Request();
        firstRequest.InitialEmployment.EmployeeCode = "MAN-DUP";
        var first = await f.Service.CreateOnboardingAsync(firstRequest, "qa-user");
        Assert.True(first.Succeeded, first.Message);
        var secondRequest = Request();
        secondRequest.PersonalDetails.FirstName = "Second";
        secondRequest.Contact.OfficialEmail = "second.work@example.test";
        secondRequest.InitialEmployment.EmployeeCode = "man-dup";

        var second = await f.Service.CreateOnboardingAsync(secondRequest, "qa-user");

        Assert.False(second.Succeeded);
        using var verify = f.Database.CreateContext(new TestTenantContext());
        Assert.Equal(1, await verify.Employees.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant && x.EmployeeCode == "MAN-DUP"));
        Assert.Equal(1, await verify.EmployeeEmploymentHistory.IgnoreQueryFilters().CountAsync(x => x.TenantId == Tenant && x.EmployeeId == first.Value!.Id));
    }

    [Fact]
    public async Task Failed_history_save_rolls_back_simple_code_sequence_increment()
    {
        using var f = await Fixture.CreateAsync(new OnboardingFailureInterceptor("history-write"));
        await f.ConfigureCodeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CreateOnboardingAsync(Request(), "qa-user"));

        using var verify = f.Database.CreateContext(new TestTenantContext(Tenant));
        var version = await verify.EmployeeCodeConfigVersions.SingleAsync(x => x.TenantId == Tenant);
        Assert.Equal(1, version.NextNumber);
        Assert.Equal(0, await verify.Employees.CountAsync(x => x.TenantId == Tenant && x.FirstName == "Asha"));
    }

    [Fact]
    public async Task Failed_history_save_rolls_back_rule_based_sequence_allocation()
    {
        using var f = await Fixture.CreateAsync(new OnboardingFailureInterceptor("history-write"));
        await f.ConfigureCodeAsync(EmployeeCodeGenerationMethod.RuleBased);
        var rule = await f.Configuration.SaveRuleAsync(null, new EmployeeCodeRuleRequest
        {
            Name = "Rollback allocation", Priority = 1, Status = EmployeeCodeRuleStatus.Active,
            Conditions = [new() { Field = EmployeeCodeConditionField.Department, ReferenceId = OrganizationTestHarness.DepartmentId(Tenant, "ENG") }],
            Segments = [new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.DepartmentCode },
                new() { SequenceOrder = 2, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 4 }]
        });
        Assert.True(rule.Succeeded, rule.Message);

        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.CreateOnboardingAsync(Request(), "qa-user"));

        using var verify = f.Database.CreateContext(new TestTenantContext(Tenant));
        Assert.Equal(0, await verify.EmployeeCodeSequences.CountAsync(x => x.TenantId == Tenant && x.EmployeeCodeRuleId == rule.Value!.Id));
        Assert.Equal(0, await verify.Employees.CountAsync(x => x.TenantId == Tenant && x.FirstName == "Asha"));
    }

    [Fact]
    public async Task Concurrent_sequence_allocations_from_isolated_sqlite_contexts_are_unique()
    {
        using var f = await Fixture.CreateAsync();
        await f.ConfigureCodeAsync(EmployeeCodeGenerationMethod.RuleBased);
        var rule = await f.Configuration.SaveRuleAsync(null, new EmployeeCodeRuleRequest
        {
            Name = "Concurrent allocation", Priority = 1, Status = EmployeeCodeRuleStatus.Active,
            Conditions = [new() { Field = EmployeeCodeConditionField.Department, ReferenceId = OrganizationTestHarness.DepartmentId(Tenant, "ENG") }],
            Segments = [new() { SequenceOrder = 1, SegmentType = EmployeeCodeSegmentType.SequentialNumber, PaddingLength = 4 }]
        });
        Assert.True(rule.Succeeded, rule.Message);
        var first = await f.Sequences.AllocateAsync(rule.Value!.Id, EmployeeCodeSequenceScope.Tenant,
            "ONBOARDING-CONCURRENT", EmployeeCodeResetPeriod.Never, "NONE");
        Assert.True(first.Succeeded, first.Message);

        using var left = f.Database.CreateIsolatedContext(f.Tenant);
        using var right = f.Database.CreateIsolatedContext(f.Tenant);
        var leftService = new EmployeeCodeSequenceService(left, f.Tenant);
        var rightService = new EmployeeCodeSequenceService(right, f.Tenant);
        var allocations = await Task.WhenAll(
            leftService.AllocateAsync(rule.Value.Id, EmployeeCodeSequenceScope.Tenant, "ONBOARDING-CONCURRENT", EmployeeCodeResetPeriod.Never, "NONE"),
            rightService.AllocateAsync(rule.Value.Id, EmployeeCodeSequenceScope.Tenant, "ONBOARDING-CONCURRENT", EmployeeCodeResetPeriod.Never, "NONE"));

        Assert.All(allocations, allocation => Assert.True(allocation.Succeeded, allocation.Message));
        Assert.Equal(new long[] { 2, 3 }, allocations.Select(x => x.Value).Order().ToArray());
    }

    private static EmployeeOnboardingRequest Request() => new()
    {
        PersonalDetails = new EmployeePersonalDetailsRequest
        {
            FirstName = "Asha", LastName = "Rao", DateOfJoining = Today, Gender = Gender.Female,
            BloodGroup = BloodGroup.OPositive, MaritalStatus = MaritalStatus.Single,
            JobStatus = "On Probation", AadhaarNumber = "999988887777", PanNumber = "ABCDE1234F"
        },
        Contact = new EmployeeContactRequest { OfficialEmail = "asha.work@example.test", PersonalEmail = "asha.home@example.test", OfficialPhone = "9998887776" },
        InitialEmployment = EmploymentRequest()
    };

    private static EmploymentChangeRequest EmploymentRequest() => new()
    {
        EffectiveFrom = Today,
        HoldingCompanyId = OrganizationTestHarness.HoldingCompanyId(Tenant, "HC01"),
        LobId = OrganizationTestHarness.LobId(Tenant, "LOB-IT"),
        OrganisationId = OrganizationTestHarness.OrganisationId(Tenant, "ORG01"),
        DepartmentId = OrganizationTestHarness.DepartmentId(Tenant, "ENG"),
        DesignationId = OrganizationTestHarness.DesignationId(Tenant, "SE"),
        GradeId = OrganizationTestHarness.GradeId(Tenant, "G1"),
        CountryLocationId = OrganizationTestHarness.CountryId("IN"),
        WorkLocationId = OrganizationTestHarness.WorkLocationId(Tenant, "WL-MUM"),
        PositionChangeReasonId = OrganizationTestHarness.PositionChangeReasonId(Tenant, "NEW_HIRE"),
        ChangeReason = EmploymentChangeReason.NewJoining,
        EmploymentType = EmploymentType.FullTime,
        EmploymentStatus = EmployeeStatus.Active
    };

    private sealed class Fixture : IDisposable
    {
        private Fixture(SqliteInMemoryDatabase database, HrmsDbContext db, TestTenantContext tenant, TestAuthorizationContext authorization)
        {
            Database = database; Db = db; Tenant = tenant; Authorization = authorization;
            var sequence = new EmployeeCodeSequenceService(db, tenant);
            Configuration = new EmployeeCodeConfigurationService(db, tenant, new EmployeeCodeConfigurationRequestValidator());
            Employment = new EmployeeEmploymentService(db, tenant, authorization, NullLogger<EmployeeEmploymentService>.Instance,
                new EmployeeCodeRuleMatcher(), new EmployeeCodeRenderer(), sequence, new FixedClock(new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero)));
            var status = new JobStatusService(db, tenant, authorization, new FixedClock(new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero)));
            Service = new EmployeeService(db, tenant, new FixedClock(new DateTimeOffset(2026, 3, 4, 9, 0, 0, TimeSpan.Zero)),
                NullLogger<EmployeeService>.Instance, jobStatusService: status, employmentService: Employment, authorization: authorization);
        }
        public SqliteInMemoryDatabase Database { get; }
        public HrmsDbContext Db { get; }
        public TestTenantContext Tenant { get; }
        public TestAuthorizationContext Authorization { get; }
        public EmployeeCodeConfigurationService Configuration { get; }
        public IEmployeeEmploymentService Employment { get; }
        public IEmployeeService Service { get; }
        public IEmployeeCodeSequenceService Sequences => new EmployeeCodeSequenceService(Db, Tenant);
        public static async Task<Fixture> CreateAsync(params IInterceptor[] interceptors)
        {
            var database = new SqliteInMemoryDatabase();
            await database.SeedAsync();
            var tenant = new TestTenantContext(EmployeeOnboardingOrchestrationTests.Tenant) { UserId = Guid.NewGuid() };
            var auth = new TestAuthorizationContext(Permissions.Employee.Create, Permissions.EmployeeSensitive.Edit);
            var db = database.CreateContext(tenant, interceptors);
            return new Fixture(database, db, tenant, auth);
        }
        public async Task ConfigureCodeAsync(EmployeeCodeGenerationMethod method = EmployeeCodeGenerationMethod.Simple,
            EmployeeCodeAssignmentMode mode = EmployeeCodeAssignmentMode.Auto, DateOnly? effectiveFrom = null,
            DateOnly? effectiveTo = null)
        {
            var result = await Configuration.SaveAsync(new EmployeeCodeConfigurationRequest
            {
                AutoGenerate = mode == EmployeeCodeAssignmentMode.Auto, AssignmentMode = mode,
                GenerationMethod = mode == EmployeeCodeAssignmentMode.Auto ? method : null,
                Prefix = "EMP", Separator = "-", NextNumber = 1, Padding = 5,
                EffectiveFrom = effectiveFrom ?? Today, EffectiveTo = effectiveTo
            });
            Assert.True(result.Succeeded, result.Message);
        }
        public async Task<Guid> CreateDraftAsync()
        {
            var personal = Request().PersonalDetails;
            var draft = await Service.CreatePersonalDetailsAsync(personal);
            Assert.True(draft.Succeeded, draft.Message);
            return draft.Value!.Id;
        }
        public void Dispose() { Db.Dispose(); Database.Dispose(); }
    }

    private sealed class TestAuthorizationContext(params string[] permissions) : ICurrentAuthorizationContext
    {
        private readonly HashSet<string> permissions = permissions.ToHashSet(StringComparer.Ordinal);
        public bool HasAnyPermission(params string[] requested) => requested.Any(permissions.Contains);
        public void SetPermissions(params string[] values) { permissions.Clear(); permissions.UnionWith(values); }
    }

    private sealed class OnboardingFailureInterceptor(string failure) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> r, CancellationToken ct = default)
        {
            var entries = e.Context!.ChangeTracker.Entries().Where(x => x.State == EntityState.Added).ToList();
            var shouldFail = failure switch
            {
                "employee-create" => entries.Any(x => x.Entity is Employee),
                "contact" => entries.Any(x => x.Entity is EmployeeContact),
                "audit" => entries.Any(x => x.Entity is EmployeeAuditLog),
                "history-write" => entries.Any(x => x.Entity is EmployeeEmploymentHistory),
                _ => false
            };
            if (shouldFail) throw new InvalidOperationException($"Injected {failure} persistence failure.");
            return base.SavingChangesAsync(e, r, ct);
        }
    }

    private sealed class TransactionCounter : DbTransactionInterceptor
    {
        public int Begins { get; private set; }
        public int Commits { get; private set; }
        public void Reset() { Begins = 0; Commits = 0; }
        public override ValueTask<DbTransaction> TransactionStartedAsync(DbConnection c, TransactionEndEventData e, DbTransaction t, CancellationToken ct = default)
        { Begins++; return base.TransactionStartedAsync(c, e, t, ct); }
        public override Task TransactionCommittedAsync(DbTransaction t, TransactionEndEventData e, CancellationToken ct = default)
        { Commits++; return base.TransactionCommittedAsync(t, e, ct); }
    }
}
