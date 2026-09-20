using System.Text;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

internal static class BankAdviceProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenantContext)
    {
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var runId = Guid.NewGuid(); var resultId = Guid.NewGuid(); var runEmployeeId = Guid.NewGuid(); var bankId = Guid.NewGuid();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            tenantContext.TenantId = tenantId;
            db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"BA{tenantId:N}"[..12], TenantName = "Bank advice provider test", Host = $"{tenantId:N}.bank.test", ShardKey = tenantId.ToString("N") });
            var employee = new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"BA-{employeeId:N}"[..12], FirstName = "Bank", LastName = "Employee, Test", Email = $"{employeeId:N}@bank.test", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active };
            db.Employees.Add(employee); db.Banks.Add(new Bank { Id = bankId, TenantId = tenantId, Code = $"B{bankId:N}"[..8], Name = "Provider Bank", IsActive = true });
            db.EmployeeBankDetails.Add(new EmployeeBankDetail { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, BankId = bankId, AccountHolderName = "Bank Employee", AccountNumber = "1234567890", IfscCode = "TEST0001", AccountPurpose = AccountPurpose.Salary, Status = BankAccountStatus.Active, IsActive = true, EffectiveFrom = new DateOnly(2026, 9, 1) });
            var structureId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var assignmentId = Guid.NewGuid(); db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = $"S{structureId:N}"[..10], Name = "Bank advice structure", IsActive = true }); db.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true }); db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new DateOnly(2026, 1, 1), MonthlyCtc = 10000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
            db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = $"BA-P-{periodId:N}"[..12], Name = "Bank advice period", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), Status = PayrollPeriodStatus.Locked });
            db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = $"BA-R-{runId:N}"[..12], Status = PayrollRunStatus.Finalized, EmployeeCount = 1 });
            db.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmploymentSnapshotDate = new DateOnly(2026, 9, 30), IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
            db.PayrollResults.Add(new PayrollResult { Id = resultId, TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new DateOnly(2026, 9, 1), PeriodEndDate = new DateOnly(2026, 9, 30), EmploymentSnapshotDate = new DateOnly(2026, 9, 30), CalendarDays = 30, EligibleDays = 30, CurrencyCode = "INR", GrossEarnings = 10000m, TotalDeductions = 1000m, NetPay = 9000m, Status = PayrollResultStatus.Calculated, IsCurrent = true });
            await db.SaveChangesAsync();
            var service = new BankAdviceService(db, tenantContext, TimeProvider.System);
            var generated = await service.GenerateAsync(runId); Assert.True(generated.Succeeded, generated.Message); var payment = Assert.Single(generated.Value!.Payments); Assert.Equal(BankAdviceValidationStatus.Valid, payment.ValidationStatus); Assert.Equal("XXXXXX7890", payment.MaskedAccountNumber); Assert.Equal(9000m, generated.Value.TotalAmount);
            var prepared = await service.PrepareAsync(generated.Value.Id); Assert.True(prepared.Succeeded, prepared.Message); var approved = await service.ApproveAsync(generated.Value.Id); Assert.True(approved.Succeeded, approved.Message);
            var export = await service.ExportAsync(generated.Value.Id); Assert.True(export.Succeeded, export.Message); Assert.Contains("XXXXXX7890", Encoding.UTF8.GetString(export.Value!.Content));
            var otherTenant = new TestTenantContext(Guid.NewGuid()); var isolated = new BankAdviceService(db, otherTenant, TimeProvider.System); Assert.False((await isolated.GetByIdAsync(generated.Value.Id)).Succeeded);
        }
        finally
        {
            await transaction.RollbackAsync();
        }
    }
}
