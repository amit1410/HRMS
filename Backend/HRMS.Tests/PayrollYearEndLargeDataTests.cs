using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollYearEndLargeDataTests
{
    [Fact]
    public async Task Year_end_tax_reconciliation_scales_to_10000_employees()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenant = new TestTenantContext(Guid.NewGuid(), Guid.NewGuid());
        var tenantId = tenant.TenantId!.Value;
        var now = DateTime.UtcNow;
        var taxYearStart = new DateOnly(2026, 4, 1);
        var taxYearEnd = new DateOnly(2027, 3, 31);
        var componentId = Guid.NewGuid();
        var structureId = Guid.NewGuid();
        var structureVersionId = Guid.NewGuid();
        var taxConfigurationId = Guid.NewGuid();
        var taxVersionId = Guid.NewGuid();
        var cycleId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        await using (var seed = database.CreateContext(tenant))
        {
            seed.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"YELD{tenantId:N}"[..12], TenantName = "Year-end large-data tenant", Host = $"{tenantId:N}.year-end-large.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
            seed.SalaryComponents.Add(new SalaryComponent { Id = componentId, TenantId = tenantId, Code = "YE-LARGE-BASIC", Name = "Year-end large-data basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryCalculationType.FixedAmount, EffectiveFrom = taxYearStart, IsActive = true, AffectsGross = true, AffectsNetPay = true });
            seed.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "YE-LARGE-STRUCT", Name = "Year-end large-data structure", IsActive = true });
            seed.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = structureVersionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = taxYearStart, IsActive = true });
            seed.StatutoryConfigurations.Add(new StatutoryConfiguration { Id = taxConfigurationId, TenantId = tenantId, JurisdictionCode = "IN", Code = "YE-LARGE-TAX", Name = "Year-end large-data tax", StatutoryType = StatutoryType.IncomeTax, IsActive = true });
            seed.StatutoryConfigurationVersions.Add(new StatutoryConfigurationVersion { Id = taxVersionId, TenantId = tenantId, StatutoryConfigurationId = taxConfigurationId, EffectiveFrom = taxYearStart, Status = StatutoryConfigurationStatus.Active, Priority = 1, ConfigurationJson = "{}" });
            seed.StatutorySlabs.Add(new StatutorySlab { Id = Guid.NewGuid(), TenantId = tenantId, StatutoryConfigurationVersionId = taxVersionId, FromAmount = 0m, Rate = 10m, FixedAmount = 0m, Sequence = 1 });
            seed.TaxDeclarationCycles.Add(new TaxDeclarationCycle { Id = cycleId, TenantId = tenantId, Code = "FY2026-LARGE", Name = "FY2026 large-data cycle", FinancialYear = 2026, DeclarationOpenDate = taxYearStart, DeclarationCloseDate = new(2026, 9, 30), ProofSubmissionOpenDate = taxYearStart, ProofSubmissionCloseDate = new(2027, 1, 31), EffectiveFrom = taxYearStart, Status = TaxDeclarationCycleStatus.Locked });
            seed.TaxDeclarationCategories.Add(new TaxDeclarationCategory { Id = categoryId, TenantId = tenantId, Code = "YE-LARGE-INV", Name = "Year-end large-data investment", CategoryType = TaxDeclarationCategoryType.Investment, RequiresProof = true, EffectiveFrom = taxYearStart, Active = true });
            seed.TaxDeclarationItems.Add(new TaxDeclarationItem { Id = itemId, TenantId = tenantId, TaxDeclarationCategoryId = categoryId, Code = "YE-LARGE-PROOF", Name = "Year-end large-data proof", RequiresProof = true, EffectiveFrom = taxYearStart, Active = true });

            var employees = new List<Employee>(10000);
            var assignments = new List<EmployeeSalaryAssignment>(10000);
            var profiles = new List<EmployeeStatutoryProfile>(9990);
            var previous = new List<YearEndTaxPreviousEmployerInput>(500);
            var declarations = new List<EmployeeTaxDeclaration>(1000);
            var declarationLines = new List<EmployeeTaxDeclarationLine>(1000);
            var proofs = new List<TaxDeclarationProof>(250);
            var firstPeriod = new PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = "YE-LARGE-APR", Name = "April 2026", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 4, 1), EndDate = new(2026, 4, 30), PayDate = new(2026, 5, 5), FiscalYear = 2026, PeriodNumber = 1, Status = PayrollPeriodStatus.Closed };
            var secondPeriod = new PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = "YE-LARGE-MAY", Name = "May 2026", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 5, 1), EndDate = new(2026, 5, 31), PayDate = new(2026, 6, 5), FiscalYear = 2026, PeriodNumber = 2, Status = PayrollPeriodStatus.Closed };
            var draftPeriod = new PayrollPeriod { Id = Guid.NewGuid(), TenantId = tenantId, Code = "YE-LARGE-DRAFT", Name = "Draft exclusion period", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 6, 1), EndDate = new(2026, 6, 30), PayDate = new(2026, 7, 5), FiscalYear = 2026, PeriodNumber = 3, Status = PayrollPeriodStatus.Draft };
            var firstRun = new PayrollRun { Id = Guid.NewGuid(), TenantId = tenantId, PayrollPeriodId = firstPeriod.Id, RunNumber = "YE-LARGE-APR-RUN", Status = PayrollRunStatus.Finalized, EmployeeCount = 10000 };
            var secondRun = new PayrollRun { Id = Guid.NewGuid(), TenantId = tenantId, PayrollPeriodId = secondPeriod.Id, RunNumber = "YE-LARGE-MAY-RUN", Status = PayrollRunStatus.Finalized, EmployeeCount = 1000 };
            var draftRun = new PayrollRun { Id = Guid.NewGuid(), TenantId = tenantId, PayrollPeriodId = draftPeriod.Id, RunNumber = "YE-LARGE-DRAFT-RUN", Status = PayrollRunStatus.Draft, EmployeeCount = 1 };
            var results = new List<PayrollResult>(11001);
            var components = new List<PayrollResultComponent>(11001);
            var statutory = new List<PayrollStatutoryResult>(11001);
            var runEmployees = new List<PayrollRunEmployee>(11001);

            for (var i = 0; i < 10000; i++)
            {
                var employeeId = Guid.NewGuid();
                var assignmentId = Guid.NewGuid();
                var employee = new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = $"YE-LD-{i:00000}", FirstName = "Large", LastName = $"Employee {i:00000}", Email = $"ye-ld-{i}@large.test", DateOfJoining = i % 20 == 0 ? new(2026, 10, 1) : new(2025, 1, 1), Status = i % 25 == 0 ? EmployeeStatus.Resigned : EmployeeStatus.Active };
                employees.Add(employee);
                assignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EffectiveFrom = taxYearStart, MonthlyCtc = 10000m, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
                if (i < 9990) profiles.Add(new EmployeeStatutoryProfile { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, JurisdictionCode = "IN", IncomeTaxApplicable = true, EffectiveFrom = taxYearStart, IsActive = true });
                if (i < 500) previous.Add(new YearEndTaxPreviousEmployerInput { Id = Guid.NewGuid(), TenantId = tenantId, RunId = firstRun.Id, EmployeeId = employeeId, EmployerName = "Previous employer", EmployerReference = $"YE-LD-PREV-{i:000}", TaxableIncome = 5000m, TaxDeducted = 300m, Status = YearEndTaxPreviousEmployerStatus.Approved, EvidenceReference = "large-test-reference", CreatedAtUtc = now });
                if (i < 1000)
                {
                    var declarationId = Guid.NewGuid();
                    var lineId = Guid.NewGuid();
                    var approved = i % 4 != 3;
                    declarations.Add(new EmployeeTaxDeclaration { Id = declarationId, TenantId = tenantId, EmployeeId = employeeId, TaxDeclarationCycleId = cycleId, Status = approved ? EmployeeTaxDeclarationStatus.Approved : EmployeeTaxDeclarationStatus.Submitted, Version = 1, ReviewedAtUtc = approved ? now : null });
                    declarationLines.Add(new EmployeeTaxDeclarationLine { Id = lineId, TenantId = tenantId, EmployeeTaxDeclarationId = declarationId, TaxDeclarationCategoryId = categoryId, TaxDeclarationItemId = itemId, DeclaredAmount = 1000m, ApprovedAmount = approved ? 800m : null, Status = approved ? EmployeeTaxDeclarationLineStatus.Approved : EmployeeTaxDeclarationLineStatus.Submitted });
                    if (approved && i % 4 == 0) proofs.Add(new TaxDeclarationProof { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeTaxDeclarationLineId = lineId, FileName = "proof.pdf", ContentType = "application/pdf", FileSize = 10, StorageReference = $"large/proof/{lineId:N}", UploadedByUserId = tenant.UserId!.Value, UploadedAtUtc = now, Status = TaxDeclarationProofStatus.Accepted, ReviewedAtUtc = now });
                }

                AddPayroll(firstRun, firstPeriod, employeeId, assignmentId, i, results, components, statutory, runEmployees, componentId, structureId, structureVersionId, taxConfigurationId, taxVersionId);
                if (i < 1000) AddPayroll(secondRun, secondPeriod, employeeId, assignmentId, i, results, components, statutory, runEmployees, componentId, structureId, structureVersionId, taxConfigurationId, taxVersionId);
                if (i == 0) AddPayroll(draftRun, draftPeriod, employeeId, assignmentId, i, results, components, statutory, runEmployees, componentId, structureId, structureVersionId, taxConfigurationId, taxVersionId, 99999m, 0m);
            }

            seed.Employees.AddRange(employees);
            seed.EmployeeSalaryAssignments.AddRange(assignments);
            seed.EmployeeStatutoryProfiles.AddRange(profiles);
            seed.EmployeeTaxDeclarations.AddRange(declarations);
            seed.EmployeeTaxDeclarationLines.AddRange(declarationLines);
            seed.TaxDeclarationProofs.AddRange(proofs);
            seed.PayrollPeriods.AddRange(firstPeriod, secondPeriod, draftPeriod);
            seed.PayrollRuns.AddRange(firstRun, secondRun, draftRun);
            seed.PayrollRunEmployees.AddRange(runEmployees);
            seed.PayrollResults.AddRange(results);
            seed.PayrollResultComponents.AddRange(components);
            seed.PayrollStatutoryResults.AddRange(statutory);
            await seed.SaveChangesAsync();

            var run = await new YearEndTaxService(seed, tenant, TimeProvider.System).CreateAsync(new HRMS.Application.DTOs.Payroll.YearEndTaxRunRequest { TaxYear = 2026, TaxYearCode = "FY2026-LARGE", StartDate = taxYearStart, EndDate = taxYearEnd });
            Assert.True(run.Succeeded, run.Message);
            var runId = run.Value!.Id;
            foreach (var row in previous) row.RunId = runId;
            seed.YearEndTaxPreviousEmployerInputs.AddRange(previous);
            await seed.SaveChangesAsync();

            var started = DateTime.UtcNow;
            var calculated = await new YearEndTaxService(seed, tenant, TimeProvider.System).CalculateAsync(runId);
            var duration = DateTime.UtcNow - started;
            Assert.True(calculated.Succeeded, calculated.Message);

            var employeeCount = await seed.YearEndTaxEmployees.CountAsync(x => x.RunId == runId);
            var adjustmentCount = await seed.YearEndTaxAdjustments.CountAsync(x => x.RunId == runId);
            var blockingCount = await seed.YearEndTaxEmployees.CountAsync(x => x.RunId == runId && x.BlockingIssueCode != null);
            var dueCount = await seed.YearEndTaxEmployees.CountAsync(x => x.RunId == runId && x.EstimatedTaxDue > 0 && x.BlockingIssueCode == null);
            var excessCount = await seed.YearEndTaxEmployees.CountAsync(x => x.RunId == runId && x.EstimatedExcessTax > 0 && x.BlockingIssueCode == null);
            var noChangeCount = await seed.YearEndTaxEmployees.CountAsync(x => x.RunId == runId && x.EstimatedTaxDue == 0 && x.EstimatedExcessTax == 0 && x.BlockingIssueCode == null);
            Assert.Equal(10000, employeeCount);
            Assert.Equal(11001, await seed.PayrollResults.CountAsync(x => x.TenantId == tenantId));
            Assert.Equal(11001, await seed.PayrollResultComponents.CountAsync(x => x.TenantId == tenantId));
            Assert.Equal(11001, await seed.PayrollStatutoryResults.CountAsync(x => x.TenantId == tenantId));
            Assert.Equal(500, await seed.YearEndTaxPreviousEmployerInputs.CountAsync(x => x.RunId == runId));
            Assert.Equal(1000, await seed.EmployeeTaxDeclarations.CountAsync(x => x.TenantId == tenantId));
            Assert.Equal(250, await seed.TaxDeclarationProofs.CountAsync(x => x.TenantId == tenantId));
            Assert.Equal(employeeCount, await seed.YearEndTaxEmployees.Where(x => x.RunId == runId).Select(x => x.EmployeeId).Distinct().CountAsync());
            Assert.Equal(adjustmentCount, await seed.YearEndTaxAdjustments.Where(x => x.RunId == runId).Select(x => x.EmployeeId).Distinct().CountAsync());
            Assert.True(blockingCount >= 10);
            Assert.Equal(dueCount, adjustmentCount);
            Assert.Equal(10000m, await seed.PayrollResults.Where(x => x.PayrollRunId == firstRun.Id && x.EmployeeId == employees[0].Id).Select(x => x.GrossEarnings).SingleAsync());

            var service = new YearEndTaxService(seed, tenant, TimeProvider.System);
            var page = await service.GetEmployeesAsync(runId, new HRMS.Application.DTOs.Payroll.YearEndTaxEmployeeQuery { Page = 1, PageSize = 100 });
            var duePage = await service.GetEmployeesAsync(runId, new HRMS.Application.DTOs.Payroll.YearEndTaxEmployeeQuery { Page = 1, PageSize = 100, HasTaxDue = true });
            Assert.True(page.Succeeded, page.Message);
            Assert.Equal(10000, page.Value!.TotalCount);
            Assert.Equal(100, page.Value.Items.Count);
            Assert.True(duePage.Succeeded, duePage.Message);
            Assert.Equal(dueCount, duePage.Value!.TotalCount);

            tenant.TenantId = Guid.NewGuid();
            await using var isolated = database.CreateContext(tenant);
            var isolatedPage = await new YearEndTaxService(isolated, tenant, TimeProvider.System).GetEmployeesAsync(runId, new HRMS.Application.DTOs.Payroll.YearEndTaxEmployeeQuery { Page = 1, PageSize = 100 });
            Assert.True(isolatedPage.Succeeded, isolatedPage.Message);
            Assert.Equal(0, isolatedPage.Value!.TotalCount);
            tenant.TenantId = tenantId;
            Assert.Equal(10000m, await seed.PayrollResults.Where(x => x.PayrollRunId == firstRun.Id && x.EmployeeId == employees[0].Id).Select(x => x.GrossEarnings).SingleAsync());

            Console.WriteLine($"Phase 7W large-data: Employees=10000, FinalizedPayrollPeriods=2, PayrollResults=11001, Declarations=1000, Proofs=250, PreviousEmployer=500, AdditionalTax={dueCount}, ExcessTax={excessCount}, NoChange={noChangeCount}, Blocking={blockingCount}, Adjustments={adjustmentCount}, Duration={duration.TotalSeconds:F2}s");
        }
    }

    private static void AddPayroll(PayrollRun run, PayrollPeriod period, Guid employeeId, Guid assignmentId, int index, List<PayrollResult> results, List<PayrollResultComponent> components, List<PayrollStatutoryResult> statutory, List<PayrollRunEmployee> runEmployees, Guid componentId, Guid structureId, Guid structureVersionId, Guid taxConfigurationId, Guid taxVersionId, decimal? gross = null, decimal? tax = null)
    {
        var resultId = Guid.NewGuid();
        var runEmployeeId = Guid.NewGuid();
        var grossAmount = gross ?? 10000m;
        var taxAmount = tax ?? (index % 10 < 3 ? 500m : index % 10 < 6 ? 1200m : 1000m);
        runEmployees.Add(new PayrollRunEmployee { Id = runEmployeeId, TenantId = run.TenantId, PayrollRunId = run.Id, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, EmploymentSnapshotDate = period.EndDate, IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
        results.Add(new PayrollResult { Id = resultId, TenantId = run.TenantId, PayrollRunId = run.Id, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = structureVersionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = period.StartDate, PeriodEndDate = period.EndDate, EmploymentSnapshotDate = period.EndDate, CalendarDays = period.EndDate.Day, EligibleDays = period.EndDate.Day, ProrationFactor = 1m, CalculationDateUtc = DateTime.UtcNow, CalculatedAtUtc = DateTime.UtcNow, CurrencyCode = "INR", GrossEarnings = grossAmount, TotalDeductions = taxAmount, NetPay = grossAmount - taxAmount, Status = PayrollResultStatus.Calculated, IsCurrent = true });
        components.Add(new PayrollResultComponent { Id = Guid.NewGuid(), TenantId = run.TenantId, PayrollResultId = resultId, SalaryComponentId = componentId, ComponentCode = "YE-LARGE-BASIC", ComponentName = "Year-end large basic", ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.FixedAmount, CalculatedAmount = grossAmount, UnproratedAmount = grossAmount, IsEarning = true, IsTaxable = true, CalculationSource = "LargeDataAcceptance", CalculationSequence = 1 });
        statutory.Add(new PayrollStatutoryResult { Id = Guid.NewGuid(), TenantId = run.TenantId, PayrollResultId = resultId, PayrollRunId = run.Id, EmployeeId = employeeId, StatutoryType = StatutoryType.IncomeTax, JurisdictionCode = "IN", StatutoryConfigurationId = taxConfigurationId, StatutoryConfigurationVersionId = taxVersionId, CalculationBasis = grossAmount, EmployeeAmount = taxAmount, TotalAmount = taxAmount, CreatedAtUtc = DateTime.UtcNow });
    }
}
