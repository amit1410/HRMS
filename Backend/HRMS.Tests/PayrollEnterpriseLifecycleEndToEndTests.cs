using HRMS.Application.DTOs.Payroll;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

/// <summary>
/// One persisted tenant/run flowing through the canonical payroll and downstream
/// services. This deliberately does not call the provider acceptance fixtures.
/// </summary>
public sealed class PayrollEnterpriseLifecycleEndToEndTests
{
    [Fact]
    public async Task One_payroll_state_flows_through_finalization_outputs_reconciliation_and_filing()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var maker = Guid.NewGuid();
        var checker = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await SeedConfigurationAsync(database, tenantId, maker, checker, employeeId);

        var tenant = new TestTenantContext(tenantId, maker);
        await using var db = database.CreateContext(tenant);
        var period = await new PayrollPeriodService(db, tenant, TimeProvider.System).CreateAsync(new PayrollPeriodRequest
        {
            Code = "E2E-2026-09", Name = "Enterprise September", PeriodType = PayrollPeriodType.Monthly,
            StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5),
            FiscalYear = 2026, PeriodNumber = 9
        });
        Assert.True(period.Succeeded, period.Message);

        var runService = new PayrollRunService(db, tenant, TimeProvider.System);
        var run = await runService.CreateAsync(new PayrollRunRequest { PayrollPeriodId = period.Value!.Id });
        Assert.True(run.Succeeded, run.Message);
        var prepared = await runService.PrepareAsync(run.Value!.Id, false);
        Assert.True(prepared.Succeeded, prepared.Message);

        var calculation = await new PayrollCalculationEngine(db, tenant, TimeProvider.System).CalculateAsync(run.Value.Id, false);
        Assert.True(calculation.Succeeded, calculation.Message);
        Assert.Equal(PayrollRunStatus.Calculated, calculation.Value!.Status);
        var result = await db.PayrollResults.AsNoTracking().SingleAsync(x => x.PayrollRunId == run.Value.Id && x.IsCurrent);
        var resultId = result.Id;
        Assert.Equal(10000m, result.GrossEarnings);
        var resultForStatutory = await db.PayrollResults.Include(x => x.Components).SingleAsync(x => x.Id == resultId);
        var statutory = await new StatutoryPayrollService(db, tenant, TimeProvider.System).CalculateAsync(resultForStatutory, resultForStatutory.Components.ToList());
        Assert.True(statutory.Succeeded, statutory.Message);
        result = await db.PayrollResults.AsNoTracking().SingleAsync(x => x.Id == resultId);
        Assert.Equal(10000m, result.NetPay);
        Assert.Single(await db.PayrollStatutoryResults.Where(x => x.PayrollResultId == resultId).ToListAsync());

        Assert.Equal(ResultStatus.ValidationFailed, (await runService.TransitionAsync(run.Value.Id, PayrollRunStatus.Approved)).Status);
        tenant.UserId = checker;
        Assert.True((await new PayrollRunService(db, tenant, TimeProvider.System).TransitionAsync(run.Value.Id, PayrollRunStatus.Approved)).Succeeded);
        Assert.True((await new PayrollRunService(db, tenant, TimeProvider.System).TransitionAsync(run.Value.Id, PayrollRunStatus.Finalized)).Succeeded);

        var output = new PayrollOutputService(db, tenant, new EmployeeIdentityResolver(db, tenant), TimeProvider.System);
        var payslip = Assert.Single((await output.GenerateAsync(run.Value.Id)).Value!);
        Assert.Equal(resultId, payslip.PayrollResultId);
        Assert.Equal(result.GrossEarnings, payslip.GrossEarnings);
        Assert.Equal(result.NetPay, payslip.NetPay);
        Assert.True((await output.PublishAsync(run.Value.Id)).Succeeded);

        tenant.UserId = maker;
        var bank = new BankAdviceService(db, tenant, TimeProvider.System);
        var bankBatch = await bank.GenerateAsync(run.Value.Id);
        Assert.True(bankBatch.Succeeded, bankBatch.Message);
        Assert.Equal(result.NetPay, bankBatch.Value!.TotalAmount);
        Assert.True((await bank.PrepareAsync(bankBatch.Value.Id)).Succeeded);
        tenant.UserId = checker;
        var bankApproved = await bank.ApproveAsync(bankBatch.Value.Id);
        Assert.True(bankApproved.Succeeded, bankApproved.Message);

        tenant.UserId = maker;
        var accounting = new PayrollAccountingService(db, tenant, TimeProvider.System);
        var journal = await accounting.GenerateAsync(run.Value.Id);
        Assert.True(journal.Succeeded, journal.Message);
        Assert.Equal(journal.Value!.TotalDebit, journal.Value.TotalCredit);
        Assert.True((await accounting.ValidateAsync(journal.Value.Id)).Succeeded);
        tenant.UserId = checker;
        Assert.True((await accounting.ApproveAsync(journal.Value.Id)).Succeeded);
        Assert.True((await accounting.PostAsync(journal.Value.Id)).Succeeded);

        tenant.UserId = maker;
        var compliance = new PayrollStatutoryComplianceService(db, tenant, TimeProvider.System);
        var compliancePeriod = await compliance.CreatePeriodAsync(new PayrollCompliancePeriodRequest
        {
            JurisdictionCode = "IN", ComplianceType = PayrollComplianceType.IncomeTaxTds,
            PeriodStart = new(2026, 9, 1), PeriodEnd = new(2026, 9, 30)
        });
        Assert.True(compliancePeriod.Succeeded, compliancePeriod.Message);
        var statutoryReturn = await compliance.GenerateAsync(compliancePeriod.Value!.Id);
        Assert.True(statutoryReturn.Succeeded, statutoryReturn.Message);
        Assert.Equal(result.GrossEarnings, statutoryReturn.Value!.GrossRelevantWages);
        Assert.True((await compliance.ValidateAsync(statutoryReturn.Value.Id)).Succeeded);
        tenant.UserId = checker;
        Assert.True((await compliance.ApproveAsync(statutoryReturn.Value.Id)).Succeeded);
        Assert.True((await compliance.MarkFiledAsync(statutoryReturn.Value.Id, "E2E-STAT-001")).Succeeded);

        var beforeCorrection = await db.PayrollResults.AsNoTracking().Where(x => x.Id == resultId).Select(x => new { x.GrossEarnings, x.NetPay }).SingleAsync();
        var adjustment = new PayrollAdjustmentService(db, tenant, TimeProvider.System, new PayrollRunService(db, tenant, TimeProvider.System));
        var correction = await adjustment.CreateAsync(new PayrollAdjustmentRequest
        {
            EmployeeId = employeeId, EffectiveDate = new(2026, 9, 30), Description = "E2E correction",
            Amount = 25m, Direction = PayrollAdjustmentDirection.Earning, ComponentCode = "E2E-CORRECTION",
            SourceType = "EnterpriseE2E", SourceReferenceId = resultId
        });
        Assert.True(correction.Succeeded, correction.Message);
        Assert.Equal(beforeCorrection.GrossEarnings, await db.PayrollResults.Where(x => x.Id == resultId).Select(x => x.GrossEarnings).SingleAsync());

        tenant.UserId = maker;
        var yearEnd = new YearEndTaxService(db, tenant, TimeProvider.System, new PayrollApprovalGuard(db, tenant));
        var yearEndRun = await yearEnd.CreateAsync(new YearEndTaxRunRequest { TaxYear = 2026, TaxYearCode = "FY2026", StartDate = new(2026, 4, 1), EndDate = new(2027, 3, 31) });
        Assert.True(yearEndRun.Succeeded, yearEndRun.Message);
        Assert.True((await yearEnd.CalculateAsync(yearEndRun.Value!.Id)).Succeeded);
        Assert.True((await yearEnd.SubmitAsync(yearEndRun.Value.Id)).Succeeded);
        tenant.UserId = checker;
        Assert.True((await yearEnd.ApproveAsync(yearEndRun.Value.Id)).Succeeded);
        Assert.True((await yearEnd.CloseAsync(yearEndRun.Value.Id)).Succeeded);

        tenant.UserId = maker;
        var filing = new StatutoryFilingService(db, tenant, TimeProvider.System, new PayrollApprovalGuard(db, tenant));
        var definition = await filing.CreateDefinitionAsync(new StatutoryFilingDefinitionRequest
        {
            Code = "E2E-STAT", Name = "Enterprise statutory package", FilingType = "TaxDeductionReturn",
            JurisdictionCode = "IN", DestinationType = StatutoryFilingDestinationType.ManualDownload
        });
        Assert.True(definition.Succeeded, definition.Message);
        var filingRun = await filing.CreateRunAsync(new StatutoryFilingRunRequest { DefinitionId = definition.Value!.Id, FilingPeriod = "2026-09" });
        Assert.True(filingRun.Succeeded, filingRun.Message);
        var package = await filing.GenerateAsync(filingRun.Value!.Id);
        Assert.True(package.Succeeded, package.Message);
        var hash = package.Value!.PackageHash;
        Assert.True((await filing.ValidateAsync(filingRun.Value.Id)).Succeeded);
        Assert.True((await filing.SubmitForApprovalAsync(filingRun.Value.Id)).Succeeded);
        tenant.UserId = checker;
        Assert.True((await filing.ApproveAsync(filingRun.Value.Id)).Succeeded);
        Assert.True((await filing.SubmitAsync(filingRun.Value.Id)).Succeeded);
        Assert.True((await filing.AcknowledgeAsync(filingRun.Value.Id, new StatutoryFilingAcknowledgementRequest { ReferenceNumber = "E2E-ACK-001" })).Succeeded);
        Assert.Equal(hash, (await filing.GetPackageAsync(filingRun.Value.Id)).Value!.PackageHash);

        var operations = new PayrollOperationsService(db, tenant);
        var health = await operations.GetProductionHealthAsync();
        var integrity = await operations.GetIntegrityAsync();
        Assert.True(health.Succeeded, health.Message);
        Assert.DoesNotContain(health.Value!.Issues, x => x.Severity == "Critical");
        Assert.True(integrity.Succeeded, integrity.Message);
        Assert.DoesNotContain(integrity.Value!.Checks, x => x.Status == "Critical");
    }

    private static async Task SeedConfigurationAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid maker, Guid checker, Guid employeeId)
    {
        var tenant = new TestTenantContext(tenantId, maker);
        await using var db = database.CreateContext(tenant);
        var structureId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var componentId = Guid.NewGuid(); var bankId = Guid.NewGuid(); var taxId = Guid.NewGuid(); var taxVersionId = Guid.NewGuid(); var expenseId = Guid.NewGuid(); var payableId = Guid.NewGuid(); var accountingId = Guid.NewGuid(); var accountingVersionId = Guid.NewGuid();
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = "E2E", TenantName = "Enterprise E2E", Host = "e2e.test", ShardKey = tenantId.ToString("N") });
        db.Users.AddRange(new User { Id = maker, TenantId = tenantId, Email = "e2e-maker@test.local", PasswordHash = "test", FirstName = "E2E", LastName = "Maker", IsActive = true }, new User { Id = checker, TenantId = tenantId, Email = "e2e-checker@test.local", PasswordHash = "test", FirstName = "E2E", LastName = "Checker", IsActive = true });
        db.PayrollControlConfigurations.Add(new PayrollControlConfiguration { Id = Guid.NewGuid(), TenantId = tenantId, RequireMakerChecker = true, PreventSelfApproval = true, UpdatedAtUtc = DateTime.UtcNow, UpdatedByUserId = maker });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "E2E-001", FirstName = "Enterprise", LastName = "Employee", Email = "e2e-employee@test.local", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active, PanNumber = "ABCDE1234F" });
        db.Banks.Add(new Bank { Id = bankId, TenantId = tenantId, Code = "E2EBANK", Name = "E2E Bank", IsActive = true });
        db.EmployeeBankDetails.Add(new EmployeeBankDetail { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, BankId = bankId, AccountHolderName = "Enterprise Employee", AccountNumber = "1234567890", IfscCode = "E2E0001", AccountPurpose = AccountPurpose.Salary, Status = BankAccountStatus.Active, IsActive = true, EffectiveFrom = new(2026, 1, 1) });
        db.SalaryComponents.Add(new SalaryComponent { Id = componentId, TenantId = tenantId, Code = "E2E-BASIC", Name = "E2E Basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = new(2026, 1, 1), IsActive = true, AffectsGross = true, AffectsNetPay = true, IsTaxable = true });
        db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "E2E-STRUCT", Name = "E2E Structure", IsActive = true });
        var version = new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2026, 1, 1), IsActive = true };
        version.Components.Add(new SalaryStructureComponent { Id = Guid.NewGuid(), TenantId = tenantId, SalaryStructureVersionId = versionId, SalaryComponentId = componentId, Sequence = 1, CalculationType = SalaryStructureCalculationType.FixedAmount, Value = 10000m, EffectiveFrom = new(2026, 1, 1), IsActive = true });
        db.SalaryStructureVersions.Add(version);
        db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new(2026, 1, 1), MonthlyCtc = 10000m, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
        db.PayrollGLAccounts.AddRange(new PayrollGLAccount { Id = expenseId, TenantId = tenantId, Code = "6000", Name = "Salary expense", AccountType = PayrollGLAccountType.Expense }, new PayrollGLAccount { Id = payableId, TenantId = tenantId, Code = "2100", Name = "Salary payable", AccountType = PayrollGLAccountType.Liability });
        db.PayrollAccountingConfigurations.Add(new PayrollAccountingConfiguration { Id = accountingId, TenantId = tenantId, Code = "E2E-GL", Name = "E2E accounting" });
        db.PayrollAccountingConfigurationVersions.Add(new PayrollAccountingConfigurationVersion { Id = accountingVersionId, TenantId = tenantId, PayrollAccountingConfigurationId = accountingId, EffectiveFrom = new(2026, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active, Mappings = { new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, SalaryComponentId = componentId, MappingType = PayrollGLMappingType.Earnings, DebitAccountId = expenseId, CreditAccountId = payableId, Priority = 1 }, new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, MappingType = PayrollGLMappingType.NetPayable, CreditAccountId = payableId, Priority = 1 } } });
        db.StatutoryConfigurations.Add(new StatutoryConfiguration { Id = taxId, TenantId = tenantId, JurisdictionCode = "IN", Code = "E2E-TAX", Name = "E2E tax", StatutoryType = StatutoryType.IncomeTax, IsActive = true });
        db.StatutoryConfigurationVersions.Add(new StatutoryConfigurationVersion { Id = taxVersionId, TenantId = tenantId, StatutoryConfigurationId = taxId, EffectiveFrom = new(2026, 1, 1), Status = StatutoryConfigurationStatus.Active, ConfigurationJson = "{}" });
        db.StatutorySlabs.Add(new StatutorySlab { Id = Guid.NewGuid(), TenantId = tenantId, StatutoryConfigurationVersionId = taxVersionId, FromAmount = 0, Rate = 10, Sequence = 1 });
        db.EmployeeStatutoryProfiles.Add(new EmployeeStatutoryProfile { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, JurisdictionCode = "IN", IncomeTaxApplicable = true, EffectiveFrom = new(2026, 1, 1), IsActive = true });
        await db.SaveChangesAsync();
    }
}
