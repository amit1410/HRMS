using System.Text;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

internal static class PayrollAccountingProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenantContext)
    {
        var tenantId = Guid.NewGuid(); var runId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var runEmployeeId = Guid.NewGuid(); var resultId = Guid.NewGuid(); var componentId = Guid.NewGuid(); var configId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var expenseId = Guid.NewGuid(); var payableId = Guid.NewGuid(); var structureId = Guid.NewGuid(); var structureVersionId = Guid.NewGuid(); var assignmentId = Guid.NewGuid();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            tenantContext.TenantId = tenantId;
            db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"GL{tenantId:N}"[..12], TenantName = "Accounting provider test", Host = $"{tenantId:N}.gl.test", ShardKey = tenantId.ToString("N") });
            db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"GL-{employeeId:N}"[..12], FirstName = "GL", LastName = "Employee", Email = $"{employeeId:N}@gl.test", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
            db.SalaryComponents.Add(new SalaryComponent { Id = componentId, TenantId = tenantId, Code = $"B{componentId:N}"[..10], Name = "Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, AffectsGross = true, AffectsNetPay = true });
            db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = $"S{structureId:N}"[..10], Name = "Payroll structure", IsActive = true });
            db.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = structureVersionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true });
            db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EffectiveFrom = new DateOnly(2026, 1, 1), MonthlyCtc = 10000m, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
            db.PayrollGLAccounts.AddRange(new PayrollGLAccount { Id = expenseId, TenantId = tenantId, Code = "6000", Name = "Salary expense", AccountType = PayrollGLAccountType.Expense }, new PayrollGLAccount { Id = payableId, TenantId = tenantId, Code = "2100", Name = "Salary payable", AccountType = PayrollGLAccountType.Liability });
            db.PayrollAccountingConfigurations.Add(new PayrollAccountingConfiguration { Id = configId, TenantId = tenantId, Code = $"CFG-{configId:N}"[..12], Name = "Payroll accounting" });
            db.PayrollAccountingConfigurationVersions.Add(new PayrollAccountingConfigurationVersion { Id = versionId, TenantId = tenantId, PayrollAccountingConfigurationId = configId, EffectiveFrom = new DateOnly(2026, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active, Mappings = { new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, SalaryComponentId = componentId, MappingType = PayrollGLMappingType.Earnings, DebitAccountId = expenseId, CreditAccountId = payableId, Priority = 1 }, new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, MappingType = PayrollGLMappingType.NetPayable, CreditAccountId = payableId, Priority = 1 } } });
            db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = $"P-{periodId:N}"[..10], Name = "September", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), Status = PayrollPeriodStatus.Locked });
            db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = $"R-{runId:N}"[..10], Status = PayrollRunStatus.Finalized, EmployeeCount = 1 });
            db.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmploymentSnapshotDate = new DateOnly(2026, 9, 30), IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
            db.PayrollResults.Add(new PayrollResult { Id = resultId, TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new DateOnly(2026, 9, 1), PeriodEndDate = new DateOnly(2026, 9, 30), EmploymentSnapshotDate = new DateOnly(2026, 9, 30), CalendarDays = 30, EligibleDays = 30, CurrencyCode = "INR", GrossEarnings = 10000m, TotalDeductions = 0m, NetPay = 10000m, Status = PayrollResultStatus.Calculated, IsCurrent = true, Components = { new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, SalaryComponentId = componentId, ComponentCode = "BASIC", ComponentName = "Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.FixedAmount, CalculatedAmount = 10000m, IsEarning = true, CalculationSequence = 1 } } });
            await db.SaveChangesAsync();
            var service = new PayrollAccountingService(db, tenantContext, TimeProvider.System); var generated = await service.GenerateAsync(runId); Assert.True(generated.Succeeded, generated.Message); Assert.Equal(generated.Value!.TotalDebit, generated.Value.TotalCredit); Assert.Equal(2, generated.Value.Lines.Count);
            var validated = await service.ValidateAsync(generated.Value.Id); Assert.True(validated.Succeeded, validated.Message); var approved = await service.ApproveAsync(generated.Value.Id); Assert.True(approved.Succeeded, approved.Message); var posted = await service.PostAsync(generated.Value.Id); Assert.True(posted.Succeeded, posted.Message); var export = await service.ExportAsync(generated.Value.Id); Assert.True(export.Succeeded, export.Message); Assert.Contains("6000", Encoding.UTF8.GetString(export.Value!.Content));
            var other = new PayrollAccountingService(db, new TestTenantContext(Guid.NewGuid()), TimeProvider.System); Assert.False((await other.GetAsync(generated.Value.Id)).Succeeded);
        }
        finally { await transaction.RollbackAsync(); }
    }
}
