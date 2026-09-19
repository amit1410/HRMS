using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollCalculationTests
{
    [Theory]
    [InlineData("10.005", "10.01")]
    [InlineData("10.004", "10.00")]
    public void Uses_away_from_zero_two_decimal_rounding(string input, string expected) => Assert.Equal(decimal.Parse(expected), PayrollRoundingPolicy.RoundMoney(decimal.Parse(input)));

    [Fact]
    public async Task Calculates_fixed_percentage_formula_gross_deductions_and_net_from_snapshot()
    {
        using var database = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var run = await SeedRun(database, tenant, employee, proration: false);
        await using var db = database.CreateContext(new TestTenantContext(tenant)); var result = await new PayrollCalculationEngine(db, new TestTenantContext(tenant), TimeProvider.System).CalculateAsync(run, false);
        Assert.True(result.Succeeded, result.Message); Assert.Equal(PayrollRunStatus.Calculated, result.Value!.Status); Assert.Equal(15000m, result.Value.GrossEarnings); Assert.Equal(1000m, result.Value.TotalDeductions); Assert.Equal(14000m, result.Value.NetPay); Assert.Equal(1, result.Value.CalculatedCount); Assert.Equal(0, result.Value.FailedCount);
        var rows = await db.PayrollResults.Include(x => x.Components).SingleAsync(); Assert.Equal(4, rows.Components.Count); Assert.All(rows.Components, x => Assert.NotEqual(Guid.Empty, x.SalaryComponentId));
    }

    [Fact]
    public async Task Prorates_proratable_components_for_mid_period_assignment_start()
    {
        using var database = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var run = await SeedRun(database, tenant, employee, proration: true);
        await using var db = database.CreateContext(new TestTenantContext(tenant)); var result = await new PayrollCalculationEngine(db, new TestTenantContext(tenant), TimeProvider.System).CalculateAsync(run, false);
        Assert.True(result.Succeeded, result.Message); Assert.Equal(7999.99m, result.Value!.GrossEarnings); Assert.Equal(533.33m, result.Value.TotalDeductions); Assert.Equal(7466.66m, result.Value.NetPay);
    }

    [Fact]
    public async Task Calculation_is_tenant_scoped_and_missing_base_is_reported()
    {
        using var database = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var other = Guid.NewGuid(); var employee = Guid.NewGuid(); var run = await SeedRun(database, tenant, employee, proration: false, missingBase: true);
        await using var otherDb = database.CreateContext(new TestTenantContext(other)); Assert.Equal(HRMS.Application.Common.ResultStatus.NotFound, (await new PayrollCalculationEngine(otherDb, new TestTenantContext(other), TimeProvider.System).CalculateAsync(run, false)).Status);
        await using var db = database.CreateContext(new TestTenantContext(tenant)); var result = await new PayrollCalculationEngine(db, new TestTenantContext(tenant), TimeProvider.System).CalculateAsync(run, false); Assert.Equal(PayrollRunStatus.Prepared, result.Value!.Status); Assert.Equal(1, result.Value.FailedCount); Assert.Equal(PayrollCalculationErrorCode.InvalidFormula, await db.PayrollCalculationErrors.Select(x => x.ErrorCode).SingleAsync());
    }

    [Fact]
    public async Task Recalculation_preserves_prior_attempt_and_is_idempotent()
    {
        using var database = new SqliteInMemoryDatabase(); var tenant = Guid.NewGuid(); var employee = Guid.NewGuid(); var run = await SeedRun(database, tenant, employee, proration: false);
        await using var db = database.CreateContext(new TestTenantContext(tenant)); var engine = new PayrollCalculationEngine(db, new TestTenantContext(tenant), TimeProvider.System);
        var first = await engine.CalculateAsync(run, false); Assert.True(first.Succeeded, first.Message);
        var firstRow = await db.PayrollResults.AsNoTracking().SingleAsync(x => x.IsCurrent); var firstAmounts = await db.PayrollResultComponents.AsNoTracking().Where(x => x.PayrollResultId == firstRow.Id).OrderBy(x => x.CalculationSequence).Select(x => x.CalculatedAmount).ToListAsync();
        var second = await engine.CalculateAsync(run, true); Assert.True(second.Succeeded, second.Message);
        var current = await db.PayrollResults.AsNoTracking().SingleAsync(x => x.IsCurrent); var currentAmounts = await db.PayrollResultComponents.AsNoTracking().Where(x => x.PayrollResultId == current.Id).OrderBy(x => x.CalculationSequence).Select(x => x.CalculatedAmount).ToListAsync();
        var historical = await db.PayrollResults.AsNoTracking().SingleAsync(x => x.Id == firstRow.Id); Assert.NotEqual(firstRow.CalculationAttemptId, current.CalculationAttemptId); Assert.Equal(2, await db.PayrollResults.CountAsync()); Assert.Equal(firstAmounts, currentAmounts); Assert.Equal(firstRow.GrossEarnings, current.GrossEarnings); Assert.Equal(firstRow.TotalDeductions, current.TotalDeductions); Assert.Equal(firstRow.NetPay, current.NetPay); Assert.False(historical.IsCurrent);
    }

    private static async Task<Guid> SeedRun(SqliteInMemoryDatabase database, Guid tenant, Guid employeeId, bool proration, bool missingBase = false)
    {
        var periodId = Guid.NewGuid(); var runId = Guid.NewGuid(); var assignmentId = Guid.NewGuid(); var structureId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        await SeedTenant(database, tenant);
        await using var db = database.CreateContext(new TestTenantContext(tenant));
        var employee = new Employee { Id = employeeId, TenantId = tenant, EmployeeCode = "E001", FirstName = "Test", LastName = "Employee", Email = employeeId + "@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active };
        var basicId = Guid.NewGuid(); var hraId = Guid.NewGuid(); var deductionId = Guid.NewGuid(); var formulaId = Guid.NewGuid();
        var components = new[] { Component(tenant, basicId, "BASIC", SalaryComponentType.Earning), Component(tenant, hraId, "HRA", SalaryComponentType.Earning), Component(tenant, deductionId, "PF", SalaryComponentType.Deduction), Component(tenant, formulaId, "BONUS", SalaryComponentType.Earning) };
        var version = new SalaryStructureVersion { Id = versionId, TenantId = tenant, SalaryStructureId = structureId, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true };
        version.Components.Add(new SalaryStructureComponent { Id = Guid.NewGuid(), TenantId = tenant, SalaryStructureVersionId = versionId, SalaryComponentId = basicId, Sequence = 1, CalculationType = SalaryStructureCalculationType.FixedAmount, Value = 10000, IsProratable = true, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true });
        version.Components.Add(new SalaryStructureComponent { Id = Guid.NewGuid(), TenantId = tenant, SalaryStructureVersionId = versionId, SalaryComponentId = hraId, Sequence = 2, CalculationType = SalaryStructureCalculationType.Percentage, Value = 40, PercentageOfComponentId = basicId, IsProratable = true, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true });
        version.Components.Add(new SalaryStructureComponent { Id = Guid.NewGuid(), TenantId = tenant, SalaryStructureVersionId = versionId, SalaryComponentId = deductionId, Sequence = 3, CalculationType = SalaryStructureCalculationType.Percentage, Value = 10, PercentageOfComponentId = basicId, IsProratable = true, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true });
        version.Components.Add(new SalaryStructureComponent { Id = Guid.NewGuid(), TenantId = tenant, SalaryStructureVersionId = versionId, SalaryComponentId = formulaId, Sequence = 4, CalculationType = SalaryStructureCalculationType.Formula, Formula = missingBase ? "MISSING + 1" : "BASIC * 0.10", IsProratable = true, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true });
        var start = proration ? new DateOnly(2026, 9, 15) : new DateOnly(2026, 1, 1); var period = new PayrollPeriod { Id = periodId, TenantId = tenant, Code = "SEP", Name = "September", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open, IsActive = true };
        var runRow = new PayrollRun { Id = runId, TenantId = tenant, PayrollPeriodId = periodId, RunNumber = "PR-2026-09-001", Status = PayrollRunStatus.Prepared, EmployeeCount = 1 };
        db.Employees.Add(employee); db.SalaryComponents.AddRange(components); db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenant, Code = "STAFF", Name = "Staff", IsActive = true }); db.SalaryStructureVersions.Add(version); db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenant, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = start, MonthlyCtc = 10000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active }); db.PayrollPeriods.Add(period); db.PayrollRuns.Add(runRow); db.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = Guid.NewGuid(), TenantId = tenant, PayrollRunId = runId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = period.EndDate, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible }); await db.SaveChangesAsync(); return runId;
    }
    private static async Task SeedTenant(SqliteInMemoryDatabase database, Guid id) { await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = id, TenantCode = "T" + id.ToString("N")[..10], TenantName = "Test", Host = id + ".test", ShardKey = id.ToString("N") }); await db.SaveChangesAsync(); }
    private static SalaryComponent Component(Guid tenant, Guid id, string code, SalaryComponentType type) => new() { Id = id, TenantId = tenant, Code = code, Name = code, ComponentType = type, CalculationType = type == SalaryComponentType.Deduction ? SalaryCalculationType.Percentage : SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, AffectsGross = type == SalaryComponentType.Earning, AffectsNetPay = true };
}
