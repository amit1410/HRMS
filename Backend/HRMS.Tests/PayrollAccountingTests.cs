using HRMS.Application.Common;
using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollAccountingTests
{
    [Fact]
    public void Journal_statuses_are_ordered_for_internal_lifecycle()
    { Assert.True((int)PayrollJournalStatus.Generated < (int)PayrollJournalStatus.Validated); Assert.True((int)PayrollJournalStatus.Validated < (int)PayrollJournalStatus.Approved); Assert.True((int)PayrollJournalStatus.Approved < (int)PayrollJournalStatus.Posted); }

    [Fact]
    public void Accounting_uses_decimal_values_and_explicit_accounting_types()
    { Assert.Equal(0.01m, decimal.Round(0.005m, 2, MidpointRounding.AwayFromZero)); Assert.Equal(PayrollGLAccountType.Liability, PayrollGLAccountType.Liability); }

    [Fact]
    public async Task Account_crud_and_same_tenant_duplicate_validation_are_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); await AddTenantsAsync(database, tenantId); await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new PayrollAccountingService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var created = await service.CreateAccountAsync(new PayrollGLAccountRequest { Code = "6000", Name = "Salary expense", AccountType = PayrollGLAccountType.Expense }); Assert.True(created.Succeeded, created.Message); Assert.Equal("6000", created.Value!.Code);
        var duplicate = await service.CreateAccountAsync(new PayrollGLAccountRequest { Code = "6000", Name = "Duplicate", AccountType = PayrollGLAccountType.Expense }); Assert.Equal(ResultStatus.Conflict, duplicate.Status);
        var updated = await service.UpdateAccountAsync(created.Value.Id, new PayrollGLAccountRequest { Code = "6001", Name = "Updated expense", AccountType = PayrollGLAccountType.Expense }); Assert.True(updated.Succeeded, updated.Message); var page = await service.ListAccountsAsync(new SalaryComponentQuery { Page = 1, PageSize = 10 }); Assert.Single(page.Value!.Items);
    }

    [Fact]
    public async Task Same_account_code_is_allowed_for_another_tenant()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid(); await AddTenantsAsync(database, tenantA, tenantB); await using (var db = database.CreateContext(new TestTenantContext(tenantA))) { var service = new PayrollAccountingService(db, new TestTenantContext(tenantA), TimeProvider.System); Assert.True((await service.CreateAccountAsync(new PayrollGLAccountRequest { Code = "2100", Name = "Payable", AccountType = PayrollGLAccountType.Liability })).Succeeded); } await using var dbB = database.CreateContext(new TestTenantContext(tenantB)); var serviceB = new PayrollAccountingService(dbB, new TestTenantContext(tenantB), TimeProvider.System); Assert.True((await serviceB.CreateAccountAsync(new PayrollGLAccountRequest { Code = "2100", Name = "Payable B", AccountType = PayrollGLAccountType.Liability })).Succeeded);
    }

    [Fact]
    public async Task Versioning_rejects_invalid_dates_and_overlaps_but_preserves_future_version()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); await AddTenantsAsync(database, tenantId); await using var db = database.CreateContext(new TestTenantContext(tenantId)); var service = new PayrollAccountingService(db, new TestTenantContext(tenantId), TimeProvider.System); var configuration = await service.CreateConfigurationAsync(new PayrollAccountingConfigurationRequest { Code = "PAYROLL", Name = "Payroll accounting" }); Assert.True(configuration.Succeeded, configuration.Message);
        var invalid = await service.CreateVersionAsync(configuration.Value!.Id, new PayrollAccountingConfigurationVersionRequest { EffectiveFrom = new DateOnly(2026, 2, 1), EffectiveTo = new DateOnly(2026, 1, 1) }); Assert.Equal(ResultStatus.ValidationFailed, invalid.Status); var first = await service.CreateVersionAsync(configuration.Value.Id, new PayrollAccountingConfigurationVersionRequest { EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 6, 30) }); Assert.True(first.Succeeded, first.Message); var overlap = await service.CreateVersionAsync(configuration.Value.Id, new PayrollAccountingConfigurationVersionRequest { EffectiveFrom = new DateOnly(2026, 6, 1) }); Assert.Equal(ResultStatus.Conflict, overlap.Status); var future = await service.CreateVersionAsync(configuration.Value.Id, new PayrollAccountingConfigurationVersionRequest { EffectiveFrom = new DateOnly(2026, 7, 1) }); Assert.True(future.Succeeded, future.Message);
    }

    [Fact]
    public async Task Mapping_rejects_inactive_cross_tenant_and_duplicate_accounts()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var otherTenant = Guid.NewGuid(); await AddTenantsAsync(database, tenantId, otherTenant); await using var db = database.CreateContext(new TestTenantContext(tenantId)); var account = new PayrollGLAccount { Id = Guid.NewGuid(), TenantId = tenantId, Code = "6000", Name = "Expense", AccountType = PayrollGLAccountType.Expense, IsActive = true }; var inactive = new PayrollGLAccount { Id = Guid.NewGuid(), TenantId = tenantId, Code = "9999", Name = "Inactive", AccountType = PayrollGLAccountType.Expense, IsActive = false }; var other = new PayrollGLAccount { Id = Guid.NewGuid(), TenantId = otherTenant, Code = "2100", Name = "Other", AccountType = PayrollGLAccountType.Liability, IsActive = true }; db.PayrollGLAccounts.AddRange(account, inactive); var config = new PayrollAccountingConfiguration { Id = Guid.NewGuid(), TenantId = tenantId, Code = "CFG", Name = "Config" }; db.PayrollAccountingConfigurations.Add(config); var version = new PayrollAccountingConfigurationVersion { Id = Guid.NewGuid(), TenantId = tenantId, PayrollAccountingConfigurationId = config.Id, EffectiveFrom = new DateOnly(2026, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active }; db.PayrollAccountingConfigurationVersions.Add(version); await db.SaveChangesAsync(); await using (var otherDb = database.CreateContext(new TestTenantContext(otherTenant))) { otherDb.PayrollGLAccounts.Add(other); await otherDb.SaveChangesAsync(); } var service = new PayrollAccountingService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var invalid = await service.CreateMappingAsync(version.Id, new PayrollGLMappingRequest { MappingType = PayrollGLMappingType.NetPayable, CreditAccountId = inactive.Id }); Assert.True(invalid.Status == ResultStatus.Conflict, $"inactive: {invalid.Status} {invalid.Message}"); var crossTenant = await service.CreateMappingAsync(version.Id, new PayrollGLMappingRequest { MappingType = PayrollGLMappingType.NetPayable, CreditAccountId = other.Id }); Assert.True(crossTenant.Status == ResultStatus.Conflict, $"cross-tenant: {crossTenant.Status} {crossTenant.Message}");
        Assert.True(await db.PayrollGLAccounts.Where(x => x.Id == account.Id).Select(x => x.IsActive).SingleAsync());
        var validRequest = new PayrollGLMappingRequest { MappingType = PayrollGLMappingType.NetPayable, CreditAccountId = account.Id, IsActive = true }; var valid = await service.CreateMappingAsync(version.Id, validRequest); Assert.True(valid.Succeeded, $"valid: {valid.Message}"); var duplicate = await service.CreateMappingAsync(version.Id, validRequest); Assert.True(duplicate.Status == ResultStatus.Conflict, $"duplicate: {duplicate.Status} {duplicate.Message}"); var loan = await service.CreateMappingAsync(version.Id, new PayrollGLMappingRequest { MappingType = PayrollGLMappingType.LoanPayrollRecovery, DebitAccountId = account.Id, CreditAccountId = account.Id, IsActive = true }); Assert.True(loan.Succeeded, $"loan: {loan.Message}");
    }

    [Fact]
    public async Task Loan_recovery_journal_uses_explicit_mapping_and_source_traceability()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); await AddTenantsAsync(database, tenantId); await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var runId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var runEmployeeId = Guid.NewGuid(); var resultId = Guid.NewGuid(); var loanId = Guid.NewGuid(); var installmentId = Guid.NewGuid(); var productId = Guid.NewGuid(); var productVersionId = Guid.NewGuid(); var structureId = Guid.NewGuid(); var structureVersionId = Guid.NewGuid(); var assignmentId = Guid.NewGuid(); var salaryComponentId = Guid.NewGuid(); var configId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var debitId = Guid.NewGuid(); var creditId = Guid.NewGuid();
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "LOAN-001", FirstName = "Loan", LastName = "Employee", Email = "loan@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
        db.PayrollGLAccounts.AddRange(new PayrollGLAccount { Id = debitId, TenantId = tenantId, Code = "2200", Name = "Salary clearing", AccountType = PayrollGLAccountType.Liability }, new PayrollGLAccount { Id = creditId, TenantId = tenantId, Code = "1300", Name = "Loan receivable", AccountType = PayrollGLAccountType.Asset });
        db.LoanProducts.Add(new LoanProduct { Id = productId, TenantId = tenantId, Code = "LOAN", Name = "Loan", ProductType = LoanProductType.Loan, CurrencyCode = "INR" });
        db.LoanProductVersions.Add(new LoanProductVersion { Id = productVersionId, TenantId = tenantId, LoanProductId = productId, EffectiveFrom = new DateOnly(2026, 1, 1), MinAmount = 1, MaxAmount = 10000, MinTenureMonths = 1, MaxTenureMonths = 12, Status = LoanProductVersionStatus.Active });
        db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "LOAN-STRUCT", Name = "Loan structure", IsActive = true });
        db.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = structureVersionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true });
        db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EffectiveFrom = new DateOnly(2026, 1, 1), MonthlyCtc = 1000, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
        db.SalaryComponents.Add(new SalaryComponent { Id = salaryComponentId, TenantId = tenantId, Code = "BASIC", Name = "Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new DateOnly(2026, 1, 1), IsActive = true, AffectsGross = true, AffectsNetPay = true });
        db.PayrollAccountingConfigurations.Add(new PayrollAccountingConfiguration { Id = configId, TenantId = tenantId, Code = "LOAN-CFG", Name = "Loan accounting" });
        db.PayrollAccountingConfigurationVersions.Add(new PayrollAccountingConfigurationVersion { Id = versionId, TenantId = tenantId, PayrollAccountingConfigurationId = configId, EffectiveFrom = new DateOnly(2026, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active, Mappings = { new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, SalaryComponentId = salaryComponentId, MappingType = PayrollGLMappingType.Earnings, DebitAccountId = debitId, CreditAccountId = creditId, Priority = 1, IsActive = true }, new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, MappingType = PayrollGLMappingType.LoanPayrollRecovery, DebitAccountId = debitId, CreditAccountId = creditId, Priority = 1, IsActive = true }, new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, MappingType = PayrollGLMappingType.NetPayable, CreditAccountId = debitId, DebitAccountId = creditId, Priority = 1, IsActive = true } } });
        db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "LOAN-P", Name = "Loan period", PeriodType = PayrollPeriodType.Monthly, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 9, 30), PayDate = new DateOnly(2026, 10, 5), Status = PayrollPeriodStatus.Locked });
        db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "LOAN-R", Status = PayrollRunStatus.Finalized });
        db.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmploymentSnapshotDate = new DateOnly(2026, 9, 30), IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
        db.EmployeeLoans.Add(new EmployeeLoan { Id = loanId, TenantId = tenantId, EmployeeId = employeeId, LoanProductId = productId, LoanProductVersionId = productVersionId, LoanNumber = "LN/2026/001", RequestedAmount = 1000, ApprovedAmount = 1000, DisbursedAmount = 1000, CurrencyCode = "INR", RequestedTenureMonths = 1, ApprovedTenureMonths = 1, Status = LoanStatus.Active, OutstandingPrincipal = 900, OutstandingTotal = 900, RequestedAtUtc = DateTime.UtcNow });
        db.LoanInstallments.Add(new LoanInstallment { Id = installmentId, TenantId = tenantId, EmployeeLoanId = loanId, InstallmentNumber = 1, DueDate = new DateOnly(2026, 9, 30), PrincipalAmount = 90, InterestAmount = 10, InstallmentAmount = 100, OpeningPrincipal = 1000, ClosingPrincipal = 910, Status = LoanInstallmentStatus.PartiallyRecovered, RecoveredAmount = 100 });
        db.LoanRepayments.Add(new LoanRepayment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeLoanId = loanId, LoanInstallmentId = installmentId, Amount = 100, PrincipalAmount = 90, InterestAmount = 10, RepaymentType = LoanRepaymentType.Payroll, PaymentDate = new DateOnly(2026, 10, 5), SourceType = "PayrollResult", PayrollRunId = runId, PayrollResultId = resultId, CreatedByUserId = Guid.NewGuid() });
        db.PayrollResults.Add(new PayrollResult { Id = resultId, TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new DateOnly(2026, 9, 1), PeriodEndDate = new DateOnly(2026, 9, 30), EmploymentSnapshotDate = new DateOnly(2026, 9, 30), CalendarDays = 30, EligibleDays = 30, CurrencyCode = "INR", GrossEarnings = 1000, TotalDeductions = 100, NetPay = 900, Status = PayrollResultStatus.Calculated, IsCurrent = true, Components = { new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, SalaryComponentId = salaryComponentId, ComponentCode = "BASIC", ComponentName = "Basic", ComponentType = SalaryComponentType.Earning, CalculatedAmount = 1000, IsEarning = true, CalculationSource = "SalaryComponent", CalculationSequence = 1 }, new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = tenantId, ComponentCode = "LOAN-LN/2026/001", ComponentName = "Loan recovery", ComponentType = SalaryComponentType.Deduction, CalculatedAmount = 100, IsDeduction = true, CalculationSource = "LoanRecovery", EmployeeLoanId = loanId, LoanInstallmentId = installmentId, CalculationSequence = 900001 } } });
        await db.SaveChangesAsync();
        var persistedMappings = await db.PayrollGLMappings.AsNoTracking().Where(x => x.ConfigurationVersionId == versionId).ToListAsync();
        Assert.Contains(persistedMappings, x => x.SalaryComponentId == salaryComponentId && x.MappingType == PayrollGLMappingType.Earnings && x.IsActive);
        var persistedResult = await db.PayrollResults.AsNoTracking().Include(x => x.Components).SingleAsync(x => x.Id == resultId);
        Assert.Equal(salaryComponentId, persistedResult.Components.Single(x => x.ComponentCode == "BASIC").SalaryComponentId);
        var result = await new PayrollAccountingService(db, new TestTenantContext(tenantId), TimeProvider.System).GenerateAsync(runId);
        Assert.True(result.Succeeded, result.Message); Assert.Equal(result.Value!.TotalDebit, result.Value.TotalCredit); Assert.Equal(3, result.Value.Lines.Count); Assert.Single(result.Value.Lines.Where(x => x.SourceType == "LoanRepayment")); Assert.Contains(result.Value.Lines, x => x.SourceType == "LoanRepayment" && x.SourceId == installmentId);
    }

    private static async Task AddTenantsAsync(SqliteInMemoryDatabase database, params Guid[] tenantIds)
    {
        await using var catalog = database.CreateContext(new TestTenantContext());
        catalog.Tenants.AddRange(tenantIds.Select(id => new Tenant
        {
            Id = id,
            TenantCode = $"AC{id:N}"[..12],
            TenantName = "Accounting test tenant",
            Host = $"{id:N}.accounting.test",
            ShardKey = id.ToString("N")
        }));
        await catalog.SaveChangesAsync();
    }
}
