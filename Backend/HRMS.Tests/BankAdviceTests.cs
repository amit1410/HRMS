using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class BankAdviceTests
{
    [Fact]
    public async Task Generates_validates_approves_and_exports_a_masked_snapshot_from_payroll_result()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var runId = Guid.NewGuid(); var resultId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var bankId = Guid.NewGuid();
        await using (var setup = database.CreateContext(new TestTenantContext()))
        {
            setup.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"BA{tenantId:N}"[..10], TenantName = "Advice tenant", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
            await setup.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Banks.Add(new Bank { Id = bankId, TenantId = tenantId, Code = "BANK", Name = "Test Bank", IsActive = true });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "BA-001", FirstName = "Bank", LastName = "Employee", Email = "bank@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
        db.EmployeeBankDetails.Add(new EmployeeBankDetail { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, BankId = bankId, AccountHolderName = "Bank Employee", AccountNumber = "1234567890", IfscCode = "TEST0001", AccountPurpose = AccountPurpose.Salary, Status = BankAccountStatus.Active, IsActive = true, EffectiveFrom = new DateOnly(2026, 9, 1) });
        var structureId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var assignmentId = Guid.NewGuid();
        db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "BA-STRUCT", Name = "Bank advice structure", IsActive = true });
        db.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true });
        db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new DateOnly(2026, 1, 1), MonthlyCtc = 10000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
        db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "BA-SEP", Name = "September", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Locked });
        db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "BA-RUN", Status = PayrollRunStatus.Finalized, EmployeeCount = 1 });
        var runEmployeeId = Guid.NewGuid(); db.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmploymentSnapshotDate = new DateOnly(2026, 9, 30), IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
        db.PayrollResults.Add(new PayrollResult { Id = resultId, TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new DateOnly(2026, 9, 1), PeriodEndDate = new DateOnly(2026, 9, 30), EmploymentSnapshotDate = new DateOnly(2026, 9, 30), CalendarDays = 30, EligibleDays = 30, CurrencyCode = "INR", GrossEarnings = 10000, TotalDeductions = 1000, NetPay = 9000, Status = PayrollResultStatus.Calculated, IsCurrent = true });
        await db.SaveChangesAsync();
        var service = new BankAdviceService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var generated = await service.GenerateAsync(runId); Assert.True(generated.Succeeded, generated.Message); var batch = Assert.Single(generated.Value!.Payments); Assert.Equal(BankAdviceValidationStatus.Valid, batch.ValidationStatus); Assert.Equal("XXXXXX7890", batch.MaskedAccountNumber);
        var prepared = await service.PrepareAsync(generated.Value.Id); Assert.True(prepared.Succeeded, prepared.Message); var approved = await service.ApproveAsync(generated.Value.Id); Assert.True(approved.Succeeded, approved.Message); var export = await service.ExportAsync(generated.Value.Id); Assert.True(export.Succeeded, export.Message); Assert.Contains("XXXXXX7890", System.Text.Encoding.UTF8.GetString(export.Value!.Content));
        Assert.Equal(BankAdviceStatus.Exported, (await service.GetByIdAsync(generated.Value.Id)).Value!.Status);
    }

    [Fact]
    public async Task Rejects_generation_for_unapproved_payroll_runs()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var runId = Guid.NewGuid();
        await using (var setup = database.CreateContext(new TestTenantContext())) { setup.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"BA{tenantId:N}"[..10], TenantName = "Advice tenant", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") }); await setup.SaveChangesAsync(); }
        await using var db = database.CreateContext(new TestTenantContext(tenantId)); db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "DRAFT", Name = "Draft", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5) }); db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "DRAFT", Status = PayrollRunStatus.Calculated }); await db.SaveChangesAsync();
        var result = await new BankAdviceService(db, new TestTenantContext(tenantId), TimeProvider.System).GenerateAsync(runId); Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, result.Status);
    }
}
