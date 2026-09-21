using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class ReimbursementAccountingTests
{
    [Fact]
    public async Task Payroll_reimbursement_journal_uses_configured_mapping_and_claim_traceability()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); await AddTenantAsync(database, tenantId);
        var employeeId = Guid.NewGuid(); var structureId = Guid.NewGuid(); var structureVersionId = Guid.NewGuid(); var assignmentId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var runId = Guid.NewGuid(); var runEmployeeId = Guid.NewGuid(); var resultId = Guid.NewGuid(); var categoryId = Guid.NewGuid(); var claimId = Guid.NewGuid(); var lineId = Guid.NewGuid(); var configId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var expenseId = Guid.NewGuid(); var payableId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "REIMB-001", FirstName = "Reimbursement", LastName = "Employee", Email = "reimbursement@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
        db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "REIMB-STRUCT", Name = "Reimbursement structure", IsActive = true });
        db.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = structureVersionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true });
        db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EffectiveFrom = new DateOnly(2026, 1, 1), MonthlyCtc = 1000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
        db.PayrollGLAccounts.AddRange(new PayrollGLAccount { Id = expenseId, TenantId = tenantId, Code = "6200", Name = "Reimbursement expense", AccountType = PayrollGLAccountType.Expense }, new PayrollGLAccount { Id = payableId, TenantId = tenantId, Code = "2200", Name = "Payroll payable", AccountType = PayrollGLAccountType.Liability });
        db.PayrollAccountingConfigurations.Add(new PayrollAccountingConfiguration { Id = configId, TenantId = tenantId, Code = "REIMB-CFG", Name = "Reimbursement accounting" });
        db.PayrollAccountingConfigurationVersions.Add(new PayrollAccountingConfigurationVersion { Id = versionId, TenantId = tenantId, PayrollAccountingConfigurationId = configId, EffectiveFrom = new DateOnly(2026, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active, Mappings = { new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, ConfigurationVersionId = versionId, MappingType = PayrollGLMappingType.ReimbursementExpense, DebitAccountId = expenseId, CreditAccountId = payableId, Priority = 1, IsActive = true }, new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, ConfigurationVersionId = versionId, MappingType = PayrollGLMappingType.NetPayable, DebitAccountId = expenseId, CreditAccountId = payableId, Priority = 1, IsActive = true } } });
        db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "REIMB-P", Name = "Reimbursement period", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Locked });
        db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "REIMB-R", Status = PayrollRunStatus.Finalized });
        db.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmploymentSnapshotDate = new DateOnly(2026, 9, 30), IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
        db.ReimbursementCategories.Add(new ReimbursementCategory { Id = categoryId, TenantId = tenantId, Code = "MEAL", Name = "Meals", CategoryType = ReimbursementCategoryType.Meal, CurrencyCode = "INR", IsActive = true });
        db.ReimbursementClaims.Add(new ReimbursementClaim { Id = claimId, TenantId = tenantId, EmployeeId = employeeId, ClaimNumber = "CLM/2026/000001", ClaimDate = new DateOnly(2026, 9, 21), CurrencyCode = "INR", TotalClaimedAmount = 100, TotalEligibleAmount = 100, TotalApprovedAmount = 100, NonTaxableAmount = 100, SettlementMethod = ReimbursementSettlementMethod.Payroll, Status = ReimbursementClaimStatus.ReadyForSettlement, Lines = { new ReimbursementClaimLine { Id = lineId, TenantId = tenantId, ReimbursementClaimId = claimId, ReimbursementCategoryId = categoryId, ExpenseDate = new DateOnly(2026, 9, 21), Description = "Approved meal", ClaimedAmount = 100, EligibleAmount = 100, ApprovedAmount = 100, NonTaxableAmount = 100, Status = ReimbursementClaimLineStatus.Approved } } });
        db.PayrollResults.Add(new PayrollResult { Id = resultId, TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new DateOnly(2026, 9, 1), PeriodEndDate = new DateOnly(2026, 9, 30), EmploymentSnapshotDate = new DateOnly(2026, 9, 30), CalendarDays = 30, EligibleDays = 30, CalculationDateUtc = DateTime.UtcNow, CalculatedAtUtc = DateTime.UtcNow, CurrencyCode = "INR", GrossEarnings = 100, NetPay = 100, Status = PayrollResultStatus.Calculated, IsCurrent = true, Components = { new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, ComponentCode = "REIMB-NONTAX", ComponentName = "Non-taxable reimbursement", ComponentType = SalaryComponentType.Reimbursement, CalculationType = SalaryStructureCalculationType.Manual, CalculatedAmount = 100, UnproratedAmount = 100, IsEarning = true, CalculationSequence = 910000, CalculationSource = "NonTaxableReimbursement", ReimbursementClaimId = claimId, ReimbursementClaimLineId = lineId } } });
        await db.SaveChangesAsync();

        var result = await new PayrollAccountingService(db, new TestTenantContext(tenantId), TimeProvider.System).GenerateAsync(runId);

        Assert.True(result.Succeeded, result.Message); Assert.Equal(result.Value!.TotalDebit, result.Value.TotalCredit); Assert.Contains(result.Value.Lines, x => x.SourceType == "PayrollResultComponent" && x.SourceId.HasValue); Assert.Equal(2, result.Value.Lines.Count);
    }

    private static async Task AddTenantAsync(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var catalog = database.CreateContext(new TestTenantContext()); catalog.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"RA{tenantId:N}"[..12], TenantName = "Reimbursement accounting", Host = $"{tenantId:N}.accounting.test", ShardKey = tenantId.ToString("N") }); await catalog.SaveChangesAsync();
    }
}
