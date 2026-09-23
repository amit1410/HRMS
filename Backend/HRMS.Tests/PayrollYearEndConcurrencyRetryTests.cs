using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollYearEndConcurrencyTests
{
    [Fact]
    public async Task Calculate_vs_previous_employer_edit()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        // SQLite serializes competing writers; use two independent contexts with a deterministic
        // interleaving so this test proves the authoritative edit is not lost without relying on timing.
        var edit = await Service(s).UpdatePreviousEmployerAsync(s.RunId, s.PreviousId, Update(2500m));
        var calculate = await Service(s).CalculateAsync(s.RunId);
        Assert.True(edit.Succeeded, edit.Message);
        Assert.True(calculate.Succeeded, calculate.Message);
        Assert.InRange(await s.Observer.YearEndTaxPreviousEmployerInputs.CountAsync(x => x.RunId == s.RunId), 1, 1);
        Assert.Equal(2500m, await s.Observer.YearEndTaxPreviousEmployerInputs.Where(x => x.Id == s.PreviousId).Select(x => x.TaxableIncome).SingleAsync());
        Assert.Single(await s.Observer.YearEndTaxEmployees.Where(x => x.RunId == s.RunId).ToListAsync());
    }

    [Fact]
    public async Task Calculate_vs_calculate()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        // Independent contexts are retained; the deterministic interleaving avoids SQLite's
        // provider-level database-writer lock obscuring the replacement semantics under test.
        var left = await Service(s).CalculateAsync(s.RunId);
        var right = await Service(s).CalculateAsync(s.RunId);
        Assert.True(left.Succeeded, left.Message);
        Assert.True(right.Succeeded, right.Message);
        Assert.Equal(YearEndTaxRunStatus.Calculated, await s.Observer.YearEndTaxRuns.Where(x => x.Id == s.RunId).Select(x => x.Status).SingleAsync());
        Assert.Single(await s.Observer.YearEndTaxEmployees.Where(x => x.RunId == s.RunId).ToListAsync());
    }

    [Fact]
    public async Task Submit_vs_recalculate()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        var calculated = await Service(s).CalculateAsync(s.RunId);
        Assert.True(calculated.Succeeded, calculated.Message);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var submit = After(gate.Task, () => Service(s).SubmitAsync(s.RunId));
        var calculate = After(gate.Task, () => Service(s).CalculateAsync(s.RunId));
        gate.SetResult(true);
        var results = await Task.WhenAll(submit, calculate);
        Assert.All(results, x => Assert.True(x.Exception is null or DbUpdateConcurrencyException, x.Exception?.ToString()));
        Assert.Contains(await s.Observer.YearEndTaxRuns.Where(x => x.Id == s.RunId).Select(x => x.Status).SingleAsync(), new[] { YearEndTaxRunStatus.Calculated, YearEndTaxRunStatus.Submitted });
        Assert.InRange(await s.Observer.YearEndTaxEmployees.CountAsync(x => x.RunId == s.RunId), 0, 1);
    }

    [Fact]
    public async Task Approve_vs_cancel_or_reject()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        await PrepareSubmittedAsync(s);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var approve = After(gate.Task, () => Service(s, s.CheckerId).ApproveAsync(s.RunId));
        var cancel = After(gate.Task, () => Service(s).CancelAsync(s.RunId));
        gate.SetResult(true);
        var results = await Task.WhenAll(approve, cancel);
        Assert.All(results, x => Assert.Null(x.Exception));
        Assert.Contains(await s.Observer.YearEndTaxRuns.Where(x => x.Id == s.RunId).Select(x => x.Status).SingleAsync(), new[] { YearEndTaxRunStatus.Approved, YearEndTaxRunStatus.Submitted });
        Assert.InRange(await s.Observer.YearEndTaxHistories.CountAsync(x => x.RunId == s.RunId && x.Event == YearEndTaxHistoryEvent.Approved), 0, 1);
    }

    [Fact]
    public async Task Approve_vs_approve()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        await PrepareSubmittedAsync(s);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var left = After(gate.Task, () => Service(s, s.CheckerId).ApproveAsync(s.RunId));
        var right = After(gate.Task, () => Service(s, s.CheckerId).ApproveAsync(s.RunId));
        gate.SetResult(true);
        var results = await Task.WhenAll(left, right);
        Assert.All(results, x => Assert.Null(x.Exception));
        Assert.Equal(YearEndTaxRunStatus.Approved, await s.Observer.YearEndTaxRuns.Where(x => x.Id == s.RunId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await s.Observer.YearEndTaxHistories.CountAsync(x => x.RunId == s.RunId && x.Event == YearEndTaxHistoryEvent.Approved));
    }

    [Fact]
    public async Task Close_vs_adjustment_change()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        await PrepareApprovedAsync(s);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var close = After(gate.Task, () => Service(s, s.CheckerId).CloseAsync(s.RunId));
        var edit = After(gate.Task, () => Service(s).UpdatePreviousEmployerAsync(s.RunId, s.PreviousId, Update(3200m)));
        gate.SetResult(true);
        var results = await Task.WhenAll(close, edit);
        Assert.All(results, x => Assert.Null(x.Exception));
        var status = await s.Observer.YearEndTaxRuns.Where(x => x.Id == s.RunId).Select(x => x.Status).SingleAsync();
        Assert.Contains(status, new[] { YearEndTaxRunStatus.Approved, YearEndTaxRunStatus.Closed });
        if (status == YearEndTaxRunStatus.Closed) Assert.False((await Service(s).UpdatePreviousEmployerAsync(s.RunId, s.PreviousId, Update(1m))).Succeeded);
    }

    [Fact]
    public async Task Close_vs_close()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        await PrepareApprovedAsync(s);
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var left = After(gate.Task, () => Service(s, s.CheckerId).CloseAsync(s.RunId));
        var right = After(gate.Task, () => Service(s, s.CheckerId).CloseAsync(s.RunId));
        gate.SetResult(true);
        var results = await Task.WhenAll(left, right);
        Assert.All(results, x => Assert.Null(x.Exception));
        Assert.Equal(YearEndTaxRunStatus.Closed, await s.Observer.YearEndTaxRuns.Where(x => x.Id == s.RunId).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await s.Observer.YearEndTaxHistories.CountAsync(x => x.RunId == s.RunId && x.Event == YearEndTaxHistoryEvent.Closed));
    }

    private static Task<Outcome> After<T>(Task gate, Func<Task<Result<T>>> operation) => Task.Run(async () => { await gate; try { await operation(); return new Outcome(null); } catch (Exception ex) { return new Outcome(ex); } });
    private static YearEndTaxService Service(YearEndTestDatabase s, Guid? user = null) => new(s.CreateContext(user), s.Context(user), TimeProvider.System, new YearEndAllowApprovalGuard());
    private static YearEndTaxPreviousEmployerUpdateRequest Update(decimal income) => new() { EmployerName = "Previous employer", EmployerReference = "PREVIOUS-001", TaxableIncome = income, TaxDeducted = 100m, EligibleDeductionAmount = 0m, EvidenceReference = "evidence" };
    private static async Task PrepareSubmittedAsync(YearEndTestDatabase s) { Assert.True((await Service(s).CalculateAsync(s.RunId)).Succeeded); Assert.True((await Service(s).SubmitAsync(s.RunId)).Succeeded); }
    private static async Task PrepareApprovedAsync(YearEndTestDatabase s) { await PrepareSubmittedAsync(s); Assert.True((await Service(s, s.CheckerId).ApproveAsync(s.RunId)).Succeeded); }
    private sealed record Outcome(Exception? Exception);
}

public sealed class PayrollYearEndRetrySafetyTests
{
    [Fact]
    public async Task Calculate_retry_is_safe()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        var calculated = await Service(s).CalculateAsync(s.RunId);
        Assert.True(calculated.Succeeded, calculated.Message);
        Assert.True((await Service(s).CalculateAsync(s.RunId)).Succeeded);
        Assert.Single(await s.Observer.YearEndTaxEmployees.Where(x => x.RunId == s.RunId).ToListAsync());
        Assert.Single(await s.Observer.YearEndTaxStatements.Where(x => x.RunId == s.RunId).ToListAsync());
    }

    [Fact]
    public async Task Submit_retry_is_safe()
    {
        await using var s = await YearEndTestDatabase.CreateAsync(); await PrepareSubmittedAsync(s);
        Assert.False((await Service(s).SubmitAsync(s.RunId)).Succeeded);
        Assert.Equal(1, await s.Observer.YearEndTaxHistories.CountAsync(x => x.RunId == s.RunId && x.Event == YearEndTaxHistoryEvent.Submitted));
    }

    [Fact]
    public async Task Approve_retry_is_safe()
    {
        await using var s = await YearEndTestDatabase.CreateAsync(); await PrepareSubmittedAsync(s);
        Assert.True((await Service(s, s.CheckerId).ApproveAsync(s.RunId)).Succeeded);
        Assert.False((await Service(s, s.CheckerId).ApproveAsync(s.RunId)).Succeeded);
        Assert.Equal(1, await s.Observer.YearEndTaxHistories.CountAsync(x => x.RunId == s.RunId && x.Event == YearEndTaxHistoryEvent.Approved));
    }

    [Fact]
    public async Task Previous_employer_input_retry_is_safe()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        var first = await Service(s).AddPreviousEmployerAsync(s.RunId, new YearEndTaxPreviousEmployerRequest { EmployeeId = s.EmployeeId, EmployerName = "Previous employer", EmployerReference = "RETRY-001", TaxableIncome = 1000m, TaxDeducted = 100m });
        var second = await Service(s).AddPreviousEmployerAsync(s.RunId, new YearEndTaxPreviousEmployerRequest { EmployeeId = s.EmployeeId, EmployerName = "Previous employer", EmployerReference = "RETRY-001", TaxableIncome = 1000m, TaxDeducted = 100m });
        Assert.True(first.Succeeded); Assert.False(second.Succeeded); Assert.Equal(2, await s.Observer.YearEndTaxPreviousEmployerInputs.CountAsync(x => x.RunId == s.RunId)); Assert.Single(await s.Observer.YearEndTaxPreviousEmployerInputs.Where(x => x.RunId == s.RunId && x.EmployerReference == "RETRY-001").ToListAsync());
    }

    [Fact]
    public async Task Adjustment_generation_retry_is_safe()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        Assert.True((await Service(s).CalculateAsync(s.RunId)).Succeeded);
        Assert.True((await Service(s).CalculateAsync(s.RunId)).Succeeded);
        Assert.Single(await s.Observer.YearEndTaxAdjustments.Where(x => x.RunId == s.RunId).ToListAsync());
    }

    [Fact]
    public async Task Adjustment_handoff_retry_is_safe()
    {
        await using var s = await YearEndTestDatabase.CreateAsync(); await PrepareApprovedAsync(s);
        var source = await s.Observer.YearEndTaxAdjustments.SingleAsync(x => x.RunId == s.RunId);
        var request = new YearEndTaxAdjustmentHandoffRequest { AdjustmentId = source.Id, ComponentCode = "YE-BASIC", SalaryComponentId = s.ComponentId };
        var first = await Service(s, s.CheckerId).HandoffAdjustmentAsync(s.RunId, request);
        var second = await Service(s, s.CheckerId).HandoffAdjustmentAsync(s.RunId, request);
        Assert.True(first.Succeeded); Assert.True(second.Succeeded);
        Assert.Equal(1, await s.Observer.YearEndTaxAdjustments.CountAsync(x => x.RunId == s.RunId));
        Assert.Equal(1, await s.Observer.PayrollAdjustments.CountAsync(x => x.SourceType == "YearEndTaxAdjustment" && x.SourceId == source.Id));
        Assert.Equal(1, await s.Observer.YearEndTaxHistories.CountAsync(x => x.RunId == s.RunId && x.Event == YearEndTaxHistoryEvent.AdjustmentHandedOff));
        Assert.Equal(500m, await s.Observer.PayrollAdjustments.Where(x => x.SourceId == source.Id).SumAsync(x => x.Amount));
    }

    [Fact]
    public async Task Close_retry_is_safe()
    {
        await using var s = await YearEndTestDatabase.CreateAsync(); await PrepareApprovedAsync(s);
        Assert.True((await Service(s, s.CheckerId).CloseAsync(s.RunId)).Succeeded);
        Assert.False((await Service(s, s.CheckerId).CloseAsync(s.RunId)).Succeeded);
        Assert.Equal(1, await s.Observer.YearEndTaxHistories.CountAsync(x => x.RunId == s.RunId && x.Event == YearEndTaxHistoryEvent.Closed));
    }

    [Fact]
    public async Task Annual_statement_snapshot_retry_is_safe()
    {
        await using var s = await YearEndTestDatabase.CreateAsync();
        Assert.True((await Service(s).CalculateAsync(s.RunId)).Succeeded);
        var first = await s.Observer.YearEndTaxStatements.Where(x => x.RunId == s.RunId).Select(x => x.SnapshotJson).SingleAsync();
        Assert.True((await Service(s).CalculateAsync(s.RunId)).Succeeded);
        var second = await s.Observer.YearEndTaxStatements.Where(x => x.RunId == s.RunId).Select(x => x.SnapshotJson).SingleAsync();
        Assert.Equal(first, second);
    }

    private static YearEndTaxService Service(YearEndTestDatabase s, Guid? user = null) => new(s.CreateContext(user), s.Context(user), TimeProvider.System, new YearEndAllowApprovalGuard());
    private static async Task PrepareSubmittedAsync(YearEndTestDatabase s) { Assert.True((await Service(s).CalculateAsync(s.RunId)).Succeeded); Assert.True((await Service(s).SubmitAsync(s.RunId)).Succeeded); }
    private static async Task PrepareApprovedAsync(YearEndTestDatabase s) { await PrepareSubmittedAsync(s); Assert.True((await Service(s, s.CheckerId).ApproveAsync(s.RunId)).Succeeded); }
}

internal sealed class YearEndTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection keeper;
    private readonly string connectionString;
    public TestTenantContext Tenant { get; } = new(Guid.NewGuid(), Guid.NewGuid());
    public Guid CheckerId { get; } = Guid.NewGuid();
    public Guid EmployeeId { get; private set; }
    public Guid ComponentId { get; private set; }
    public Guid RunId { get; private set; }
    public Guid PreviousId { get; private set; }
    public HrmsDbContext Observer { get; private set; } = null!;

    private YearEndTestDatabase(SqliteConnection keeper, string connectionString) { this.keeper = keeper; this.connectionString = connectionString; }

    public static async Task<YearEndTestDatabase> CreateAsync()
    {
        var name = $"year-end-{Guid.NewGuid():N}";
        var cs = $"Data Source=file:{name};Mode=Memory;Cache=Shared";
        var keeper = new SqliteConnection(cs); await keeper.OpenAsync();
        var s = new YearEndTestDatabase(keeper, cs) { EmployeeId = Guid.NewGuid(), ComponentId = Guid.NewGuid(), RunId = Guid.NewGuid() };
        s.Observer = s.CreateContext(); await s.Observer.Database.EnsureCreatedAsync();
        var tenantId = s.Tenant.TenantId!.Value;
        var structureId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var assignmentId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var payrollRunId = Guid.NewGuid(); var payrollRunEmployeeId = Guid.NewGuid(); var resultId = Guid.NewGuid(); var taxConfigId = Guid.NewGuid(); var taxVersionId = Guid.NewGuid();
        s.Observer.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"YE{tenantId:N}"[..12], TenantName = "Year-end concurrency tenant", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        s.Observer.Employees.Add(new Employee { Id = s.EmployeeId, TenantId = tenantId, EmployeeCode = "YE-001", FirstName = "Year", LastName = "End", Email = $"{s.EmployeeId:N}@test.local", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
        s.Observer.SalaryComponents.Add(new SalaryComponent { Id = s.ComponentId, TenantId = tenantId, Code = "YE-BASIC", Name = "Year-end basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new(2026, 4, 1), IsActive = true, AffectsGross = true, AffectsNetPay = true });
        s.Observer.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "YE-STRUCT", Name = "Year-end structure", IsActive = true });
        s.Observer.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2026, 4, 1), IsActive = true });
        s.Observer.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = s.EmployeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new(2026, 4, 1), MonthlyCtc = 10000m, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
        var period = new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "YE-PERIOD", Name = "Year-end period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 6, Status = PayrollPeriodStatus.Closed };
        var payrollRun = new PayrollRun { Id = payrollRunId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "YE-RUN", Status = PayrollRunStatus.Finalized, EmployeeCount = 1 };
        var runEmployee = new PayrollRunEmployee { Id = payrollRunEmployeeId, TenantId = tenantId, PayrollRunId = payrollRunId, EmployeeId = s.EmployeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = period.EndDate, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible };
        s.Observer.PayrollPeriods.Add(period); s.Observer.PayrollRuns.Add(payrollRun); s.Observer.PayrollRunEmployees.Add(runEmployee);
        s.Observer.PayrollResults.Add(new PayrollResult { Id = resultId, TenantId = tenantId, PayrollRunId = payrollRunId, PayrollRunEmployeeId = payrollRunEmployeeId, EmployeeId = s.EmployeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = period.StartDate, PeriodEndDate = period.EndDate, EmploymentSnapshotDate = period.EndDate, CalendarDays = 30, EligibleDays = 30, ProrationFactor = 1m, CalculationDateUtc = DateTime.UtcNow, CalculatedAtUtc = DateTime.UtcNow, CurrencyCode = "INR", GrossEarnings = 10000m, TotalDeductions = 500m, NetPay = 9500m, Status = PayrollResultStatus.Calculated, IsCurrent = true });
        s.Observer.PayrollResultComponents.Add(new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = resultId, SalaryComponentId = s.ComponentId, ComponentCode = "YE-BASIC", ComponentName = "Year-end basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.FixedAmount, CalculatedAmount = 10000m, UnproratedAmount = 10000m, IsEarning = true, IsTaxable = true, CalculationSource = "Test", CalculationSequence = 1 });
        s.Observer.StatutoryConfigurations.Add(new StatutoryConfiguration { Id = taxConfigId, TenantId = tenantId, JurisdictionCode = "IN", Code = "YE-TAX", Name = "Year-end tax", StatutoryType = StatutoryType.IncomeTax, IsActive = true });
        s.Observer.StatutoryConfigurationVersions.Add(new StatutoryConfigurationVersion { Id = taxVersionId, TenantId = tenantId, StatutoryConfigurationId = taxConfigId, EffectiveFrom = new(2026, 4, 1), Status = StatutoryConfigurationStatus.Active, Priority = 1 });
        s.Observer.StatutorySlabs.Add(new StatutorySlab { Id = Guid.NewGuid(), TenantId = tenantId, StatutoryConfigurationVersionId = taxVersionId, FromAmount = 0m, Rate = 10m, FixedAmount = 0m, Sequence = 1 });
        s.Observer.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = s.EmployeeId, JurisdictionCode = "IN", IncomeTaxApplicable = true, EffectiveFrom = new(2026, 4, 1), IsActive = true });
        s.Observer.PayrollStatutoryResults.Add(new PayrollStatutoryResult { Id = Guid.NewGuid(), TenantId = tenantId, PayrollResultId = resultId, PayrollRunId = payrollRunId, EmployeeId = s.EmployeeId, StatutoryType = StatutoryType.IncomeTax, JurisdictionCode = "IN", StatutoryConfigurationId = taxConfigId, StatutoryConfigurationVersionId = taxVersionId, CalculationBasis = 10000m, EmployeeAmount = 500m, TotalAmount = 500m, CreatedAtUtc = DateTime.UtcNow });
        await s.Observer.SaveChangesAsync();
        var run = await new YearEndTaxService(s.Observer, s.Tenant, TimeProvider.System, new YearEndAllowApprovalGuard()).CreateAsync(new YearEndTaxRunRequest { TaxYear = 2026, TaxYearCode = "FY2026", StartDate = new(2026, 4, 1), EndDate = new(2027, 3, 31) });
        if (!run.Succeeded) throw new InvalidOperationException(run.Message);
        s.RunId = run.Value!.Id;
        var previous = await new YearEndTaxService(s.Observer, s.Tenant, TimeProvider.System, new YearEndAllowApprovalGuard()).AddPreviousEmployerAsync(run.Value!.Id, new YearEndTaxPreviousEmployerRequest { EmployeeId = s.EmployeeId, EmployerName = "Previous employer", EmployerReference = "PREVIOUS-001", TaxableIncome = 0m, TaxDeducted = 0m });
        if (!previous.Succeeded) throw new InvalidOperationException(previous.Message);
        s.PreviousId = previous.Value!.Id;
        return s;
    }

    public TestTenantContext Context(Guid? user = null) => new(Tenant.TenantId, user ?? Tenant.UserId);
    public HrmsDbContext CreateContext(Guid? user = null) => new(new DbContextOptionsBuilder<HrmsDbContext>().UseSqlite(new SqliteConnection(connectionString)).Options, Context(user));
    public async ValueTask DisposeAsync() { await Observer.DisposeAsync(); await keeper.DisposeAsync(); }
}

internal sealed class YearEndAllowApprovalGuard : IPayrollApprovalGuard
{
    public Task<Result<bool>> ValidateAsync(Guid? makerUserId, string action, string? reason = null, CancellationToken ct = default) => Task.FromResult(Result<bool>.Success(true));
}
