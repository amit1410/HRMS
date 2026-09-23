using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

internal static class PayrollYearEndProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant)
    {
        var tenantId = Guid.NewGuid();
        var makerId = Guid.NewGuid();
        var checkerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var componentId = Guid.NewGuid();
        var structureId = Guid.NewGuid();
        var structureVersionId = Guid.NewGuid();
        var assignmentId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var finalizedRunId = Guid.NewGuid();
        var finalizedRunEmployeeId = Guid.NewGuid();
        var finalizedResultId = Guid.NewGuid();
        var draftRunId = Guid.NewGuid();
        var draftRunEmployeeId = Guid.NewGuid();
        var draftResultId = Guid.NewGuid();
        var taxConfigurationId = Guid.NewGuid();
        var taxVersionId = Guid.NewGuid();

        tenant.TenantId = tenantId;
        tenant.UserId = makerId;

        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"YE{tenantId:N}"[..12], TenantName = "Year-end provider tenant", Host = $"{tenantId:N}.year-end.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
        db.Users.AddRange(
            new User { Id = makerId, TenantId = tenantId, Email = $"{makerId:N}@year-end.test", PasswordHash = "provider-test-hash", FirstName = "Year", LastName = "Maker", IsActive = true },
            new User { Id = checkerId, TenantId = tenantId, Email = $"{checkerId:N}@year-end.test", PasswordHash = "provider-test-hash", FirstName = "Year", LastName = "Checker", IsActive = true });
        db.PayrollControlConfigurations.Add(new PayrollControlConfiguration { Id = Guid.NewGuid(), TenantId = tenantId, RequireMakerChecker = true, PreventSelfApproval = true, UpdatedAtUtc = DateTime.UtcNow, UpdatedByUserId = makerId });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "YE-001", FirstName = "Year", LastName = "End", Email = $"{employeeId:N}@year-end.test", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
        db.SalaryComponents.Add(new SalaryComponent { Id = componentId, TenantId = tenantId, Code = "YE-BASIC", Name = "Year-end taxable earnings", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new(2026, 4, 1), IsActive = true, AffectsGross = true, AffectsNetPay = true });
        db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "YE-STRUCT", Name = "Year-end structure", IsActive = true });
        db.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = structureVersionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2026, 4, 1), IsActive = true });
        db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EffectiveFrom = new(2026, 4, 1), MonthlyCtc = 10000m, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });

        var period = new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "YE-SEP-2026", Name = "September 2026", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 6, Status = PayrollPeriodStatus.Closed };
        var finalizedRun = new PayrollRun { Id = finalizedRunId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "YE-FINAL-2026", Status = PayrollRunStatus.Finalized, EmployeeCount = 1 };
        var finalizedRunEmployee = new PayrollRunEmployee { Id = finalizedRunEmployeeId, TenantId = tenantId, PayrollRunId = finalizedRunId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EmploymentSnapshotDate = period.EndDate, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible };
        var finalizedResult = new PayrollResult { Id = finalizedResultId, TenantId = tenantId, PayrollRunId = finalizedRunId, PayrollRunEmployeeId = finalizedRunEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = period.StartDate, PeriodEndDate = period.EndDate, EmploymentSnapshotDate = period.EndDate, CalendarDays = 30, EligibleDays = 30, ProrationFactor = 1m, CalculationDateUtc = DateTime.UtcNow, CalculatedAtUtc = DateTime.UtcNow, CurrencyCode = "INR", GrossEarnings = 10000m, TotalDeductions = 1700m, NetPay = 8300m, Status = PayrollResultStatus.Calculated, IsCurrent = true, Components = [new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = finalizedResultId, SalaryComponentId = componentId, ComponentCode = "YE-BASIC", ComponentName = "Year-end taxable earnings", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.FixedAmount, CalculatedAmount = 10000m, UnproratedAmount = 10000m, IsEarning = true, IsTaxable = true, CalculationSource = "ProviderAcceptance", CalculationSequence = 1 }] };

        var draftPeriodId = Guid.NewGuid();
        var draftPeriod = new PayrollPeriod { Id = draftPeriodId, TenantId = tenantId, Code = "YE-DRAFT-2026", Name = "Draft period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31), PayDate = new(2026, 11, 5), FiscalYear = 2026, PeriodNumber = 7, Status = PayrollPeriodStatus.Draft };
        var draftRun = new PayrollRun { Id = draftRunId, TenantId = tenantId, PayrollPeriodId = draftPeriodId, RunNumber = "YE-DRAFT-2026", Status = PayrollRunStatus.Draft, EmployeeCount = 1 };
        var draftRunEmployee = new PayrollRunEmployee { Id = draftRunEmployeeId, TenantId = tenantId, PayrollRunId = draftRunId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EmploymentSnapshotDate = draftPeriod.EndDate, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible };
        var draftResult = new PayrollResult { Id = draftResultId, TenantId = tenantId, PayrollRunId = draftRunId, PayrollRunEmployeeId = draftRunEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = draftPeriod.StartDate, PeriodEndDate = draftPeriod.EndDate, EmploymentSnapshotDate = draftPeriod.EndDate, CalendarDays = 31, EligibleDays = 31, ProrationFactor = 1m, CalculationDateUtc = DateTime.UtcNow, CalculatedAtUtc = DateTime.UtcNow, CurrencyCode = "INR", GrossEarnings = 99999m, TotalDeductions = 0m, NetPay = 99999m, Status = PayrollResultStatus.Calculated, IsCurrent = true, Components = [new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = draftResultId, SalaryComponentId = componentId, ComponentCode = "YE-BASIC", ComponentName = "Draft taxable earnings", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.FixedAmount, CalculatedAmount = 99999m, UnproratedAmount = 99999m, IsEarning = true, IsTaxable = true, CalculationSource = "DraftProviderAcceptance", CalculationSequence = 1 }] };

        db.PayrollPeriods.AddRange(period, draftPeriod);
        db.PayrollRuns.AddRange(finalizedRun, draftRun);
        db.PayrollRunEmployees.AddRange(finalizedRunEmployee, draftRunEmployee);
        db.PayrollResults.AddRange(finalizedResult, draftResult);
        db.StatutoryConfigurations.Add(new StatutoryConfiguration { Id = taxConfigurationId, TenantId = tenantId, JurisdictionCode = "IN", Code = "YE-INCOME-TAX", Name = "Configured year-end tax", StatutoryType = StatutoryType.IncomeTax, IsActive = true });
        db.StatutoryConfigurationVersions.Add(new StatutoryConfigurationVersion { Id = taxVersionId, TenantId = tenantId, StatutoryConfigurationId = taxConfigurationId, EffectiveFrom = new(2026, 4, 1), Status = StatutoryConfigurationStatus.Active, Priority = 1, ConfigurationJson = "{}" });
        db.StatutorySlabs.Add(new StatutorySlab { Id = Guid.NewGuid(), TenantId = tenantId, StatutoryConfigurationVersionId = taxVersionId, FromAmount = 0m, ToAmount = null, Rate = 10m, FixedAmount = 0m, Sequence = 1 });
        db.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, JurisdictionCode = "IN", IncomeTaxApplicable = true, EffectiveFrom = new(2026, 4, 1), IsActive = true });
        db.PayrollStatutoryResults.Add(new PayrollStatutoryResult { Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = finalizedResultId, PayrollRunId = finalizedRunId, EmployeeId = employeeId, StatutoryType = StatutoryType.IncomeTax, JurisdictionCode = "IN", StatutoryConfigurationId = taxConfigurationId, StatutoryConfigurationVersionId = taxVersionId, CalculationBasis = 10000m, EmployeeAmount = 700m, TotalAmount = 700m, CreatedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await SeedApprovedDeclarationAsync(db, tenant, tenantId, employeeId, makerId);

        var yearEnd = new YearEndTaxService(db, tenant, TimeProvider.System, new PayrollApprovalGuard(db, tenant));
        var run = await yearEnd.CreateAsync(new YearEndTaxRunRequest { TaxYear = 2026, TaxYearCode = "FY2026", StartDate = new(2026, 4, 1), EndDate = new(2027, 3, 31) });
        Assert.True(run.Succeeded, run.Message);
        var runId = run.Value!.Id;
        var previous = await yearEnd.AddPreviousEmployerAsync(runId, new YearEndTaxPreviousEmployerRequest { EmployeeId = employeeId, EmployerName = "Previous Employer", EmployerReference = "PREV-001", TaxableIncome = 5000m, TaxDeducted = 300m, EvidenceReference = "prev-proof-001" });
        Assert.True(previous.Succeeded, previous.Message);
        var previousRow = await db.YearEndTaxPreviousEmployerInputs.SingleAsync(x => x.Id == previous.Value!.Id);
        previousRow.Status = YearEndTaxPreviousEmployerStatus.Approved;
        await db.SaveChangesAsync();

        var calculated = await yearEnd.CalculateAsync(runId);
        Assert.True(calculated.Succeeded, calculated.Message);
        var employee = await db.YearEndTaxEmployees.SingleAsync(x => x.RunId == runId && x.EmployeeId == employeeId);
        Assert.Equal(10000m, employee.YtdGross);
        Assert.Equal(10000m, employee.YtdTaxableIncome);
        Assert.Equal(700m, employee.YtdTaxDeducted);
        Assert.Equal(800m, employee.ApprovedDeclarationAmount);
        Assert.Equal(800m, employee.ApprovedProofAmount);
        Assert.Equal(5000m, employee.PreviousEmployerTaxableIncome);
        Assert.Equal(300m, employee.PreviousEmployerTaxDeducted);
        Assert.Equal(14200m, employee.FinalTaxableIncome);
        Assert.Equal(1420m, employee.FinalTaxLiability);
        Assert.Equal(420m, employee.EstimatedTaxDue);
        Assert.Equal(0m, employee.EstimatedExcessTax);
        Assert.DoesNotContain(99999m.ToString(), employee.CalculationSnapshotJson, StringComparison.Ordinal);

        var beforeGross = await db.PayrollResults.Where(x => x.Id == finalizedResultId).Select(x => x.GrossEarnings).SingleAsync();
        Assert.True((await yearEnd.SubmitAsync(runId)).Succeeded);
        var makerApproval = await yearEnd.ApproveAsync(runId);
        Assert.False(makerApproval.Succeeded);
        tenant.UserId = checkerId;
        var approved = await new YearEndTaxService(db, tenant, TimeProvider.System, new PayrollApprovalGuard(db, tenant)).ApproveAsync(runId);
        Assert.True(approved.Succeeded, approved.Message);
        var recommendation = await db.YearEndTaxAdjustments.SingleAsync(x => x.RunId == runId);
        var handoff = await new YearEndTaxService(db, tenant, TimeProvider.System, new PayrollApprovalGuard(db, tenant)).HandoffAdjustmentAsync(runId, new YearEndTaxAdjustmentHandoffRequest { AdjustmentId = recommendation.Id, ComponentCode = "YE-BASIC", SalaryComponentId = componentId });
        Assert.True(handoff.Succeeded, handoff.Message);
        Assert.NotNull(handoff.Value!.PayrollAdjustmentId);
        Assert.Equal(beforeGross, await db.PayrollResults.Where(x => x.Id == finalizedResultId).Select(x => x.GrossEarnings).SingleAsync());
        var closed = await new YearEndTaxService(db, tenant, TimeProvider.System, new PayrollApprovalGuard(db, tenant)).CloseAsync(runId);
        Assert.True(closed.Succeeded, closed.Message);
        Assert.Equal(YearEndTaxRunStatus.Closed, closed.Value!.Status);
        Assert.False((await new YearEndTaxService(db, tenant, TimeProvider.System, new PayrollApprovalGuard(db, tenant)).CalculateAsync(runId)).Succeeded);
        Assert.False((await new YearEndTaxService(db, tenant, TimeProvider.System, new PayrollApprovalGuard(db, tenant)).UpdatePreviousEmployerAsync(runId, previous.Value.Id, new YearEndTaxPreviousEmployerUpdateRequest { EmployerName = "Changed", TaxableIncome = 1, TaxDeducted = 1 })).Succeeded);
        Assert.True((await new YearEndTaxService(db, new TestTenantContext(Guid.NewGuid(), checkerId), TimeProvider.System).GetAsync(runId)).Status == ResultStatus.NotFound);
        var history = await new YearEndTaxService(db, tenant, TimeProvider.System).GetHistoryAsync(runId);
        Assert.True(history.Succeeded);
        Assert.Contains(history.Value!, x => x.Event == YearEndTaxHistoryEvent.Created);
        Assert.Contains(history.Value!, x => x.Event == YearEndTaxHistoryEvent.Calculated);
        Assert.Contains(history.Value!, x => x.Event == YearEndTaxHistoryEvent.Submitted);
        Assert.Contains(history.Value!, x => x.Event == YearEndTaxHistoryEvent.Approved);
        Assert.Contains(history.Value!, x => x.Event == YearEndTaxHistoryEvent.AdjustmentHandedOff);
        Assert.Contains(history.Value!, x => x.Event == YearEndTaxHistoryEvent.Closed);
    }

    private static async Task SeedApprovedDeclarationAsync(HrmsDbContext db, TestTenantContext tenant, Guid tenantId, Guid employeeId, Guid makerId)
    {
        var cycleId = Guid.NewGuid(); var categoryId = Guid.NewGuid(); var itemId = Guid.NewGuid(); var declarationId = Guid.NewGuid(); var lineId = Guid.NewGuid(); var proofId = Guid.NewGuid();
        db.TaxDeclarationCycles.Add(new TaxDeclarationCycle { Id = cycleId, TenantId = tenantId, Code = "FY2026", Name = "FY 2026", FinancialYear = 2026, DeclarationOpenDate = new(2026, 4, 1), DeclarationCloseDate = new(2026, 9, 30), ProofSubmissionOpenDate = new(2026, 4, 1), ProofSubmissionCloseDate = new(2026, 10, 31), EffectiveFrom = new(2026, 4, 1), Status = TaxDeclarationCycleStatus.Locked });
        db.TaxDeclarationCategories.Add(new TaxDeclarationCategory { Id = categoryId, TenantId = tenantId, Code = "YE-INV", Name = "Year-end investment", CategoryType = TaxDeclarationCategoryType.Investment, RequiresProof = true, EffectiveFrom = new(2026, 4, 1) });
        db.TaxDeclarationItems.Add(new TaxDeclarationItem { Id = itemId, TenantId = tenantId, TaxDeclarationCategoryId = categoryId, Code = "YE-PROOF", Name = "Approved proof", RequiresProof = true, EffectiveFrom = new(2026, 4, 1) });
        db.EmployeeTaxDeclarations.Add(new EmployeeTaxDeclaration { Id = declarationId, TenantId = tenantId, EmployeeId = employeeId, TaxDeclarationCycleId = cycleId, Status = EmployeeTaxDeclarationStatus.Approved, ReviewedByUserId = makerId, ReviewedAtUtc = DateTime.UtcNow });
        db.EmployeeTaxDeclarationLines.Add(new EmployeeTaxDeclarationLine { Id = lineId, TenantId = tenantId, EmployeeTaxDeclarationId = declarationId, TaxDeclarationCategoryId = categoryId, TaxDeclarationItemId = itemId, DeclaredAmount = 1000m, ApprovedAmount = 800m, Status = EmployeeTaxDeclarationLineStatus.Approved });
        db.TaxDeclarationProofs.Add(new TaxDeclarationProof { Id = proofId, TenantId = tenantId, EmployeeTaxDeclarationLineId = lineId, FileName = "proof.pdf", ContentType = "application/pdf", FileSize = 10, StorageReference = "provider/proof.pdf", UploadedByUserId = makerId, UploadedAtUtc = DateTime.UtcNow, Status = TaxDeclarationProofStatus.Accepted, ReviewedByUserId = makerId, ReviewedAtUtc = DateTime.UtcNow, Hash = "provider-proof-hash" });
        await db.SaveChangesAsync();
    }
}

public sealed class MySqlPayrollYearEndIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_payroll_year_end_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Payroll Year-End acceptance not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var name = $"HRMS_Phase7W_YearEnd_{Guid.NewGuid():N}";
        var connection = new MySqlConnectionStringBuilder(normalized) { Database = name };
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var create = server.CreateCommand(); create.CommandText = $"CREATE DATABASE `{name}`"; await create.ExecuteNonQueryAsync(); }
            var tenant = new TestTenantContext();
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
            await db.Database.MigrateAsync();
            await PayrollYearEndProviderAcceptance.RunAsync(db, tenant);
        }
        finally
        {
            await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class SqlServerPayrollYearEndIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerPayrollYearEndIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerPayrollYearEndFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_payroll_year_end_provider_acceptance()
    {
        var tenant = new TestTenantContext();
        await using var db = fixture.CreateContext(tenant);
        await PayrollYearEndProviderAcceptance.RunAsync(db, tenant);
    }
}

public sealed class SqlServerPayrollYearEndFactAttribute : FactAttribute
{
    public SqlServerPayrollYearEndFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable))
        ? $"SQL Server Payroll Year-End tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent."
        : null;
}
