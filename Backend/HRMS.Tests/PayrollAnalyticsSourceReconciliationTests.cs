using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollAnalyticsSourceReconciliationTests
{
    [Fact]
    public Task Reimbursement_matrix_missing_source_is_a_finding() => AssertMissingSource("Reimbursement", "ReimbursementSourceMismatch");

    [Fact]
    public Task Loan_matrix_missing_source_is_a_finding() => AssertMissingSource("LoanRecovery", "LoanSourceMismatch");

    [Fact]
    public Task Variable_pay_matrix_missing_source_is_a_finding() => AssertMissingSource("VariablePay", "VariablePaySourceMismatch");

    [Fact]
    public Task Payroll_adjustment_matrix_missing_source_is_a_finding() => AssertMissingSource("PayrollAdjustment", "AdjustmentSourceMismatch");

    [Fact]
    public async Task Statutory_matrix_missing_register_is_a_finding_without_rate_recalculation()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var configurationId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            seed.StatutoryConfigurations.Add(new StatutoryConfiguration { Id = configurationId, TenantId = ids.TenantId, StatutoryType = StatutoryType.ProvidentFund, Code = "PF-MATRIX", Name = "Persisted PF configuration" });
            seed.StatutoryConfigurationVersions.Add(new StatutoryConfigurationVersion { Id = versionId, TenantId = ids.TenantId, StatutoryConfigurationId = configurationId, EffectiveFrom = new(2026, 1, 1), ConfigurationJson = "{\"source\":\"test\"}" });
            seed.PayrollStatutoryResults.Add(new PayrollStatutoryResult { Id = Guid.NewGuid(), TenantId = ids.TenantId, PayrollResultId = ids.ResultId, PayrollRunId = ids.RunId, EmployeeId = ids.EmployeeId, StatutoryType = StatutoryType.ProvidentFund, StatutoryConfigurationId = configurationId, StatutoryConfigurationVersionId = versionId, CalculationBasis = 10000m, EmployeeAmount = 1200m, EmployerAmount = 1200m, TotalAmount = 2400m, CalculationMetadata = "{\"persisted\":true}" });
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == "MissingStatutorySource");
        Assert.Equal(10000m, await db.PayrollResults.Where(x => x.Id == ids.ResultId).Select(x => x.GrossEarnings).SingleAsync());
    }

    [Fact]
    public async Task Statutory_matrix_reconciles_pf_esi_pt_and_tds_by_persisted_type()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var definitions = new[]
        {
            (StatutoryType.ProvidentFund, "PF", 1200m),
            (StatutoryType.Esi, "ESI", 600m),
            (StatutoryType.ProfessionalTax, "PT", 200m),
            (StatutoryType.IncomeTax, "TDS", 700m)
        };
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            foreach (var definition in definitions)
            {
                var configId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var complianceId = Guid.NewGuid(); var batchId = Guid.NewGuid(); var returnEmployeeId = Guid.NewGuid(); var statutoryId = Guid.NewGuid();
                seed.StatutoryConfigurations.Add(new StatutoryConfiguration { Id = configId, TenantId = ids.TenantId, StatutoryType = definition.Item1, Code = $"{definition.Item2}-MATRIX", Name = definition.Item2 });
                seed.StatutoryConfigurationVersions.Add(new StatutoryConfigurationVersion { Id = versionId, TenantId = ids.TenantId, StatutoryConfigurationId = configId, EffectiveFrom = new(2026, 1, 1) });
                seed.PayrollStatutoryResults.Add(new PayrollStatutoryResult { Id = statutoryId, TenantId = ids.TenantId, PayrollResultId = ids.ResultId, PayrollRunId = ids.RunId, EmployeeId = ids.EmployeeId, StatutoryType = definition.Item1, StatutoryConfigurationId = configId, StatutoryConfigurationVersionId = versionId, CalculationBasis = 10000m, EmployeeAmount = definition.Item3, EmployerAmount = 0m, TotalAmount = definition.Item3, CalculationMetadata = "{\"persisted\":true}" });
                seed.PayrollCompliancePeriods.Add(new PayrollCompliancePeriod { Id = complianceId, TenantId = ids.TenantId, ComplianceType = definition.Item1 switch { StatutoryType.ProvidentFund => PayrollComplianceType.ProvidentFund, StatutoryType.Esi => PayrollComplianceType.Esi, StatutoryType.ProfessionalTax => PayrollComplianceType.ProfessionalTax, _ => PayrollComplianceType.IncomeTaxTds }, PeriodStart = new(2026, 9, 1), PeriodEnd = new(2026, 9, 30) });
                seed.PayrollStatutoryReturnBatches.Add(new PayrollStatutoryReturnBatch { Id = batchId, TenantId = ids.TenantId, PayrollCompliancePeriodId = complianceId, ComplianceType = definition.Item1 switch { StatutoryType.ProvidentFund => PayrollComplianceType.ProvidentFund, StatutoryType.Esi => PayrollComplianceType.Esi, StatutoryType.ProfessionalTax => PayrollComplianceType.ProfessionalTax, _ => PayrollComplianceType.IncomeTaxTds }, BatchNumber = $"{definition.Item2}-MATRIX", EmployeeCount = 1, EmployeeContribution = definition.Item3, TotalDeduction = definition.Item3, TotalPayable = definition.Item3 });
                seed.PayrollStatutoryReturnEmployees.Add(new PayrollStatutoryReturnEmployee { Id = returnEmployeeId, TenantId = ids.TenantId, PayrollStatutoryReturnBatchId = batchId, EmployeeId = ids.EmployeeId, EmployeeCodeSnapshot = "SRC-EMP", EmployeeNameSnapshot = "Source Employee", EmployeeContribution = definition.Item3, DeductionAmount = definition.Item3, PayableAmount = definition.Item3, Sequence = 1 });
                seed.PayrollStatutoryReturnSources.Add(new PayrollStatutoryReturnSource { Id = Guid.NewGuid(), TenantId = ids.TenantId, PayrollStatutoryReturnBatchId = batchId, PayrollStatutoryReturnEmployeeId = returnEmployeeId, PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, PayrollStatutoryResultId = statutoryId, Amount = definition.Item3, SourceType = definition.Item2 });
            }
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.DoesNotContain(result.Value!.Findings, x => x.ControlCode.Contains("StatutorySourceMismatch", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_cross_module_sources_are_reported_individually_without_mutation()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            seed.PayrollResultComponents.AddRange(
                SourceComponent(ids, "Reimbursement", 100m, 1),
                SourceComponent(ids, "LoanRecovery", 100m, 2),
                SourceComponent(ids, "VariablePay", 100m, 3),
                SourceComponent(ids, "PayrollAdjustment", 100m, 4));
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == "ReimbursementSourceMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "LoanSourceMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "VariablePaySourceMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "AdjustmentSourceMismatch");

        await using var verify = database.CreateContext(new TestTenantContext(ids.TenantId));
        Assert.Equal(10000m, await verify.PayrollResults.Where(x => x.Id == ids.ResultId).Select(x => x.GrossEarnings).SingleAsync());
    }

    [Fact]
    public async Task Final_settlement_exact_sources_pass_and_duplicate_source_is_detected()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var settlementId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            seed.FinalSettlementCases.Add(new FinalSettlementCase { Id = settlementId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, PayrollRunId = ids.RunId, SeparationDate = new(2026, 9, 30), LastWorkingDate = new(2026, 9, 30), SettlementDate = new(2026, 10, 5), EmployeeCodeSnapshot = "SRC-EMP", EmployeeNameSnapshot = "Source Employee", GrossPayable = 9000m, NetSettlement = 9000m, Status = FinalSettlementStatus.Finalized });
            seed.FinalSettlementLines.Add(new FinalSettlementLine { Id = Guid.NewGuid(), TenantId = ids.TenantId, FinalSettlementCaseId = settlementId, LineType = FinalSettlementLineType.Gratuity, ComponentCode = "GRATUITY", Description = "Persisted gratuity", Amount = 9000m, IsEarning = true, SourceType = "Gratuity", SourceId = sourceId, Sequence = 1 });
            await seed.SaveChangesAsync();
        }

        await using (var db = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            var exact = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
            Assert.True(exact.Succeeded, exact.Message);
            Assert.DoesNotContain(exact.Value!.Findings, x => x.ControlCode == "FinalSettlementSourceMismatch" || x.ControlCode == "DuplicateFinalSettlementSource");
        }

        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            seed.FinalSettlementLines.Add(new FinalSettlementLine { Id = Guid.NewGuid(), TenantId = ids.TenantId, FinalSettlementCaseId = settlementId, LineType = FinalSettlementLineType.Gratuity, ComponentCode = "GRATUITY-2", Description = "Duplicate gratuity", Amount = 1m, IsEarning = true, SourceType = "Gratuity", SourceId = sourceId, Sequence = 2 });
            await seed.SaveChangesAsync();
        }

        await using var verify = database.CreateContext(new TestTenantContext(ids.TenantId));
        var duplicate = await new PayrollAnalyticsService(verify, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(duplicate.Succeeded, duplicate.Message);
        Assert.Contains(duplicate.Value!.Findings, x => x.ControlCode == "DuplicateFinalSettlementSource");
    }

    [Fact]
    public async Task Exact_persisted_reimbursement_loan_variable_pay_and_adjustment_sources_reconcile()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var claimId = Guid.NewGuid(); var lineId = Guid.NewGuid(); var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid(); var productVersionId = Guid.NewGuid(); var loanId = Guid.NewGuid(); var installmentId = Guid.NewGuid();
        var planId = Guid.NewGuid(); var planVersionId = Guid.NewGuid(); var awardId = Guid.NewGuid(); var adjustmentId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            seed.ReimbursementCategories.Add(new ReimbursementCategory { Id = categoryId, TenantId = ids.TenantId, Code = "MATRIX", Name = "Matrix", CategoryType = ReimbursementCategoryType.Other });
            seed.ReimbursementClaims.Add(new ReimbursementClaim { Id = claimId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, ClaimNumber = "MATRIX-CLAIM", ClaimDate = new(2026, 9, 10), TotalClaimedAmount = 100m, TotalEligibleAmount = 100m, TotalApprovedAmount = 100m, SettledAmount = 100m, Status = ReimbursementClaimStatus.Settled });
            seed.ReimbursementClaimLines.Add(new ReimbursementClaimLine { Id = lineId, TenantId = ids.TenantId, ReimbursementClaimId = claimId, ReimbursementCategoryId = categoryId, ExpenseDate = new(2026, 9, 10), Description = "Matrix reimbursement", ClaimedAmount = 100m, EligibleAmount = 100m, ApprovedAmount = 100m, NonTaxableAmount = 100m, Status = ReimbursementClaimLineStatus.Settled });
            seed.ReimbursementSettlements.Add(new ReimbursementSettlement { Id = Guid.NewGuid(), TenantId = ids.TenantId, ReimbursementClaimId = claimId, ReimbursementClaimLineId = lineId, EmployeeId = ids.EmployeeId, SettlementType = ReimbursementSettlementType.Payroll, Amount = 100m, NonTaxableAmount = 100m, SettlementDate = new(2026, 9, 30), PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId });

            seed.LoanProducts.Add(new LoanProduct { Id = productId, TenantId = ids.TenantId, Code = "MATRIX-LOAN", Name = "Matrix loan", ProductType = LoanProductType.Loan });
            seed.LoanProductVersions.Add(new LoanProductVersion { Id = productVersionId, TenantId = ids.TenantId, LoanProductId = productId, EffectiveFrom = new(2026, 1, 1), MinAmount = 1m, MaxAmount = 100000m, MinTenureMonths = 1, MaxTenureMonths = 12, InterestMethod = LoanInterestMethod.None, InterestRate = 0m, Status = LoanProductVersionStatus.Active });
            seed.EmployeeLoans.Add(new EmployeeLoan { Id = loanId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, LoanProductId = productId, LoanProductVersionId = productVersionId, LoanNumber = "MATRIX-LOAN-1", RequestedAmount = 100m, ApprovedAmount = 100m, DisbursedAmount = 100m, RequestedTenureMonths = 1, ApprovedTenureMonths = 1, InterestMethod = LoanInterestMethod.None, Status = LoanStatus.Active, OutstandingPrincipal = 0m, OutstandingTotal = 0m, RequestedAtUtc = DateTime.UtcNow });
            seed.LoanInstallments.Add(new LoanInstallment { Id = installmentId, TenantId = ids.TenantId, EmployeeLoanId = loanId, InstallmentNumber = 1, DueDate = new(2026, 9, 30), PrincipalAmount = 100m, InstallmentAmount = 100m, ClosingPrincipal = 0m, Status = LoanInstallmentStatus.Recovered, PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, RecoveredAmount = 100m });
            seed.LoanRepayments.Add(new LoanRepayment { Id = Guid.NewGuid(), TenantId = ids.TenantId, EmployeeLoanId = loanId, LoanInstallmentId = installmentId, Amount = 100m, PrincipalAmount = 100m, RepaymentType = LoanRepaymentType.Payroll, PaymentDate = new(2026, 9, 30), SourceType = "PayrollResult", PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, CreatedByUserId = Guid.Empty });

            seed.VariablePayPlans.Add(new VariablePayPlan { Id = planId, TenantId = ids.TenantId, Code = "MATRIX-VP", Name = "Matrix variable pay", PlanType = VariablePayPlanType.OneTimeAward });
            seed.VariablePayPlanVersions.Add(new VariablePayPlanVersion { Id = planVersionId, TenantId = ids.TenantId, VariablePayPlanId = planId, EffectiveFrom = new(2026, 1, 1), Status = VariablePayPlanVersionStatus.Published, CalculationMethod = VariablePayCalculationMethod.FixedAmount, FixedAmount = 100m });
            seed.VariablePayAwards.Add(new VariablePayAward { Id = awardId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, VariablePayPlanId = planId, VariablePayPlanVersionId = planVersionId, AwardNumber = "MATRIX-VP-1", AwardPeriodFrom = new(2026, 9, 1), AwardPeriodTo = new(2026, 9, 30), EligibilityDate = new(2026, 9, 30), SalaryBasisAmount = 10000m, CalculatedAmount = 100m, ApprovedAmount = 100m, SettledAmount = 100m, Status = VariablePayAwardStatus.Approved });
            seed.VariablePaySettlements.Add(new VariablePaySettlement { Id = Guid.NewGuid(), TenantId = ids.TenantId, VariablePayAwardId = awardId, EmployeeId = ids.EmployeeId, SettlementType = VariablePaySettlementType.Payroll, Amount = 100m, SettlementDate = new(2026, 9, 30), PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, CreatedAtUtc = DateTime.UtcNow });

            seed.PayrollAdjustments.Add(new PayrollAdjustment { Id = adjustmentId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, SourceType = "Matrix", SourceId = Guid.NewGuid(), AdjustmentType = PayrollAdjustmentType.AdditionalEarning, ComponentCode = "MATRIX-ADJ", Description = "Matrix adjustment", Amount = 100m, AdjustmentNumber = "MATRIX-ADJ-1", EffectiveDate = new(2026, 9, 30), Status = PayrollAdjustmentStatus.Applied, AppliedAmount = 100m });
            seed.PayrollAdjustmentApplications.Add(new PayrollAdjustmentApplication { Id = Guid.NewGuid(), TenantId = ids.TenantId, PayrollAdjustmentId = adjustmentId, PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, AppliedAmount = 100m, AppliedDate = new(2026, 9, 30) });
            seed.PayrollResultComponents.AddRange(SourceComponent(ids, "Reimbursement", 100m, 30), SourceComponent(ids, "LoanRecovery", 100m, 31), SourceComponent(ids, "VariablePay", 100m, 32), SourceComponent(ids, "PayrollAdjustment", 100m, 33));
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.DoesNotContain(result.Value!.Findings, x => x.ControlCode is "ReimbursementSourceMismatch" or "LoanSourceMismatch" or "VariablePaySourceMismatch" or "AdjustmentSourceMismatch" or "DuplicateReimbursementSettlement" or "DuplicateLoanRecovery" or "DuplicateVariablePaySettlement" or "DuplicatePayrollAdjustmentApplication");
    }

    [Fact]
    public async Task Final_settlement_cross_source_composition_reconciles_persisted_lines_once()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var settlementId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            seed.FinalSettlementCases.Add(new FinalSettlementCase { Id = settlementId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, PayrollRunId = ids.RunId, SeparationDate = new(2026, 9, 30), LastWorkingDate = new(2026, 9, 30), SettlementDate = new(2026, 10, 5), EmployeeCodeSnapshot = "SRC-EMP", EmployeeNameSnapshot = "Source Employee", GrossPayable = 9000m, NetSettlement = 9000m, Status = FinalSettlementStatus.Finalized });
            var lines = new[]
            {
                (FinalSettlementLineType.Gratuity, "Gratuity", 2000m, true),
                (FinalSettlementLineType.LeaveEncashment, "LeaveEncashment", 1500m, true),
                (FinalSettlementLineType.Reimbursement, "Reimbursement", 1000m, true),
                (FinalSettlementLineType.Bonus, "VariablePay", 1000m, true),
                (FinalSettlementLineType.NoticePay, "NoticePay", 4500m, true),
                (FinalSettlementLineType.NoticeRecovery, "NoticeRecovery", 500m, false),
                (FinalSettlementLineType.Recovery, "LoanRecovery", 300m, false),
                (FinalSettlementLineType.Recovery, "PayrollAdjustment", 200m, false)
            };
            var sequence = 1;
            foreach (var line in lines)
                seed.FinalSettlementLines.Add(new FinalSettlementLine { Id = Guid.NewGuid(), TenantId = ids.TenantId, FinalSettlementCaseId = settlementId, LineType = line.Item1, ComponentCode = line.Item2, Description = line.Item2, Amount = line.Item3, IsEarning = line.Item4, IsDeduction = !line.Item4, SourceType = line.Item2, SourceId = Guid.NewGuid(), Sequence = sequence++ });
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.DoesNotContain(result.Value!.Findings, x => x.ControlCode == "FinalSettlementSourceMismatch" || x.ControlCode == "DuplicateFinalSettlementSource");
    }

    [Fact]
    public async Task Statutory_negative_matrix_reports_each_persisted_type_mismatch_without_recalculation()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var definitions = new[]
        {
            (StatutoryType.ProvidentFund, "PF", 1200m),
            (StatutoryType.Esi, "ESI", 600m),
            (StatutoryType.ProfessionalTax, "PT", 200m),
            (StatutoryType.IncomeTax, "TDS", 700m)
        };
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            foreach (var definition in definitions)
            {
                var configId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var complianceId = Guid.NewGuid(); var batchId = Guid.NewGuid(); var returnEmployeeId = Guid.NewGuid(); var statutoryId = Guid.NewGuid();
                seed.StatutoryConfigurations.Add(new StatutoryConfiguration { Id = configId, TenantId = ids.TenantId, StatutoryType = definition.Item1, Code = $"NEG-{definition.Item2}", Name = definition.Item2 });
                seed.StatutoryConfigurationVersions.Add(new StatutoryConfigurationVersion { Id = versionId, TenantId = ids.TenantId, StatutoryConfigurationId = configId, EffectiveFrom = new(2026, 1, 1) });
                seed.PayrollStatutoryResults.Add(new PayrollStatutoryResult { Id = statutoryId, TenantId = ids.TenantId, PayrollResultId = ids.ResultId, PayrollRunId = ids.RunId, EmployeeId = ids.EmployeeId, StatutoryType = definition.Item1, StatutoryConfigurationId = configId, StatutoryConfigurationVersionId = versionId, CalculationBasis = 10000m, EmployeeAmount = definition.Item3, TotalAmount = definition.Item3, CalculationMetadata = "{\"persisted\":true}" });
                seed.PayrollCompliancePeriods.Add(new PayrollCompliancePeriod { Id = complianceId, TenantId = ids.TenantId, ComplianceType = definition.Item1 switch { StatutoryType.ProvidentFund => PayrollComplianceType.ProvidentFund, StatutoryType.Esi => PayrollComplianceType.Esi, StatutoryType.ProfessionalTax => PayrollComplianceType.ProfessionalTax, _ => PayrollComplianceType.IncomeTaxTds }, PeriodStart = new(2026, 9, 1), PeriodEnd = new(2026, 9, 30) });
                seed.PayrollStatutoryReturnBatches.Add(new PayrollStatutoryReturnBatch { Id = batchId, TenantId = ids.TenantId, PayrollCompliancePeriodId = complianceId, ComplianceType = definition.Item1 switch { StatutoryType.ProvidentFund => PayrollComplianceType.ProvidentFund, StatutoryType.Esi => PayrollComplianceType.Esi, StatutoryType.ProfessionalTax => PayrollComplianceType.ProfessionalTax, _ => PayrollComplianceType.IncomeTaxTds }, BatchNumber = $"NEG-{definition.Item2}", EmployeeCount = 1, EmployeeContribution = definition.Item3, TotalDeduction = definition.Item3, TotalPayable = definition.Item3 });
                seed.PayrollStatutoryReturnEmployees.Add(new PayrollStatutoryReturnEmployee { Id = returnEmployeeId, TenantId = ids.TenantId, PayrollStatutoryReturnBatchId = batchId, EmployeeId = ids.EmployeeId, EmployeeCodeSnapshot = "SRC-EMP", EmployeeNameSnapshot = "Source Employee", EmployeeContribution = definition.Item3, DeductionAmount = definition.Item3, PayableAmount = definition.Item3, Sequence = 1 });
                seed.PayrollStatutoryReturnSources.Add(new PayrollStatutoryReturnSource { Id = Guid.NewGuid(), TenantId = ids.TenantId, PayrollStatutoryReturnBatchId = batchId, PayrollStatutoryReturnEmployeeId = returnEmployeeId, PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, PayrollStatutoryResultId = statutoryId, Amount = definition.Item3 + 1m, SourceType = definition.Item2 });
            }
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == "ProvidentFundStatutorySourceMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "EsiStatutorySourceMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "ProfessionalTaxStatutorySourceMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "IncomeTaxStatutorySourceMismatch");
        await using var verify = database.CreateContext(new TestTenantContext(ids.TenantId));
        Assert.Equal(10000m, await verify.PayrollResults.Where(x => x.Id == ids.ResultId).Select(x => x.GrossEarnings).SingleAsync());
    }

    [Fact]
    public async Task Accounting_and_payroll_adjustment_orphan_reversal_links_are_findings()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var journalId = Guid.NewGuid(); var lineId = Guid.NewGuid(); var accountId = Guid.NewGuid(); var adjustmentId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            var journal = await seed.PayrollJournalBatches.SingleAsync(x => x.PayrollRunId == ids.RunId);
            seed.PayrollGLAccounts.Add(new PayrollGLAccount { Id = accountId, TenantId = ids.TenantId, Code = "NEG-GL", Name = "Negative linkage", AccountType = PayrollGLAccountType.Expense });
            seed.PayrollJournalLines.Add(new PayrollJournalLine { Id = lineId, TenantId = ids.TenantId, PayrollJournalBatchId = journal.Id, Sequence = 99, PayrollGLAccountId = accountId, AccountCodeSnapshot = "NEG-GL", AccountNameSnapshot = "Negative linkage", Description = "Orphan reversal", Credit = 1m, SourceType = "PayrollReversal", SourceId = Guid.NewGuid() });
            seed.PayrollJournalLineSources.Add(new PayrollJournalLineSource { Id = Guid.NewGuid(), TenantId = ids.TenantId, PayrollJournalLineId = lineId, SourceType = "PayrollReversal", SourceId = Guid.NewGuid(), Amount = 1m });
            seed.PayrollAdjustments.Add(new PayrollAdjustment { Id = adjustmentId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, SourceType = "PayrollReversal", SourceId = Guid.NewGuid(), OriginalPayrollRunId = Guid.NewGuid(), TargetPayrollRunId = ids.RunId, AdjustmentType = PayrollAdjustmentType.ManualPayrollCorrection, ComponentCode = "NEG-REV", Description = "Orphan adjustment reversal", Amount = 1m, AppliedAmount = 1m, AdjustmentNumber = "NEG-REV-1", EffectiveDate = new(2026, 9, 30), Status = PayrollAdjustmentStatus.Applied });
            seed.PayrollAdjustmentApplications.Add(new PayrollAdjustmentApplication { Id = Guid.NewGuid(), TenantId = ids.TenantId, PayrollAdjustmentId = adjustmentId, PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, AppliedAmount = 1m, AppliedDate = new(2026, 9, 30) });
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == "BrokenAccountingReversalLinkage");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "BrokenPayrollAdjustmentReversalLinkage");
    }

    [Fact]
    public async Task Final_settlement_category_sources_report_missing_and_amount_mismatch_without_recalculation()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var settlementId = Guid.NewGuid(); var policyId = Guid.NewGuid(); var policyVersionId = Guid.NewGuid(); var leaveTypeId = Guid.NewGuid();
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            seed.FinalSettlementCases.Add(new FinalSettlementCase { Id = settlementId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, PayrollRunId = ids.RunId, SeparationDate = new(2026, 9, 30), LastWorkingDate = new(2026, 9, 30), SettlementDate = new(2026, 10, 5), EmployeeCodeSnapshot = "SRC-EMP", EmployeeNameSnapshot = "Source Employee", GrossPayable = 1000m, NetSettlement = 1000m, Status = FinalSettlementStatus.Finalized });
            seed.GratuityPolicies.Add(new GratuityPolicy { Id = policyId, TenantId = ids.TenantId, Code = "NEG-GRAT", Name = "Negative gratuity" });
            seed.GratuityPolicyVersions.Add(new GratuityPolicyVersion { Id = policyVersionId, TenantId = ids.TenantId, GratuityPolicyId = policyId, EffectiveFrom = new(2026, 1, 1), FormulaType = GratuityFormulaType.FixedAmount, FixedAmount = 100m });
            seed.LeaveTypes.Add(new LeaveType { Id = leaveTypeId, TenantId = ids.TenantId, Code = "NEG-LEAVE", Name = "Negative leave", IsPaid = true });
            seed.GratuityCalculations.Add(new GratuityCalculation { Id = Guid.NewGuid(), TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, FinalSettlementId = settlementId, GratuityPolicyId = policyId, GratuityPolicyVersionId = policyVersionId, SeparationReason = SeparationReason.Resignation, ServiceStartDate = new(2025, 1, 1), ServiceEndDate = new(2026, 9, 30), TotalServiceDays = 638, TotalServiceMonths = 21, EligibleServiceUnits = 1, AppliedRoundingRule = "test", WageBasisType = GratuityWageBasisType.Basic, WageBasisAmount = 100m, GrossCalculatedAmount = 100m, FinalGratuityAmount = 100m, TaxableAmount = 0m, NonTaxableAmount = 100m, CalculationDateUtc = DateTime.UtcNow, Status = SeparationBenefitCalculationStatus.Finalized });
            seed.LeaveEncashmentCalculations.Add(new LeaveEncashmentCalculation { Id = Guid.NewGuid(), TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, FinalSettlementId = settlementId, LeaveTypeId = leaveTypeId, EligibleDays = 2, EncashableDays = 2, WageBasisAmount = 100m, Divisor = 30m, GrossAmount = 200m, TaxableAmount = 0m, NonTaxableAmount = 200m, SourceBalanceReference = "NEG", CalculationDateUtc = DateTime.UtcNow });
            seed.NoticeSettlementCalculations.Add(new NoticeSettlementCalculation { Id = Guid.NewGuid(), TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, FinalSettlementId = settlementId, Type = NoticeSettlementType.NoticePay, RequiredDays = 30, ServedDays = 0, DifferenceDays = 30, WageBasisAmount = 300m, Divisor = 30m, Amount = 300m, TaxableAmount = 300m, NonTaxableAmount = 0m, CalculationDateUtc = DateTime.UtcNow });
            seed.FinalSettlementLines.Add(new FinalSettlementLine { Id = Guid.NewGuid(), TenantId = ids.TenantId, FinalSettlementCaseId = settlementId, LineType = FinalSettlementLineType.Gratuity, ComponentCode = "GRATUITY", Description = "Wrong gratuity", Amount = 99m, IsEarning = true, SourceType = "Gratuity", SourceId = Guid.NewGuid(), Sequence = 1 });
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == "FinalSettlementGratuityMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "MissingFinalSettlementLeaveEncashment");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "MissingFinalSettlementNoticePay");
        await using var verify = database.CreateContext(new TestTenantContext(ids.TenantId));
        Assert.Equal(10000m, await verify.PayrollResults.Where(x => x.Id == ids.ResultId).Select(x => x.GrossEarnings).SingleAsync());
    }

    [Fact]
    public async Task Final_settlement_reimbursement_loan_variable_pay_and_adjustment_mismatches_are_findings()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var sourceIds = await SeedFinalSettlementSources(database, ids, useMissingSourceIds: false);
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            var settlementId = Guid.NewGuid();
            seed.FinalSettlementCases.Add(new FinalSettlementCase { Id = settlementId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, PayrollRunId = ids.RunId, SeparationDate = new(2026, 9, 30), LastWorkingDate = new(2026, 9, 30), SettlementDate = new(2026, 10, 5), EmployeeCodeSnapshot = "SRC-EMP", EmployeeNameSnapshot = "Source Employee", GrossPayable = 400m, NetSettlement = 400m, Status = FinalSettlementStatus.Finalized });
            seed.FinalSettlementLines.AddRange(
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Reimbursement, "Reimbursement", sourceIds.Reimbursement, 99m, true, 1),
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Recovery, "LoanRecovery", sourceIds.Loan, 99m, false, 2),
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Bonus, "VariablePay", sourceIds.VariablePay, 99m, true, 3),
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Recovery, "PayrollAdjustment", sourceIds.Adjustment, 99m, false, 4));
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == "FinalSettlementReimbursementMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "FinalSettlementLoanMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "FinalSettlementVariablePayMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "FinalSettlementAdjustmentMismatch");
    }

    [Fact]
    public async Task Final_settlement_reimbursement_loan_variable_pay_and_adjustment_missing_sources_are_findings()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var sourceIds = await SeedFinalSettlementSources(database, ids, useMissingSourceIds: true);
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            var settlementId = Guid.NewGuid();
            seed.FinalSettlementCases.Add(new FinalSettlementCase { Id = settlementId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, PayrollRunId = ids.RunId, SeparationDate = new(2026, 9, 30), LastWorkingDate = new(2026, 9, 30), SettlementDate = new(2026, 10, 5), EmployeeCodeSnapshot = "SRC-EMP", EmployeeNameSnapshot = "Source Employee", GrossPayable = 400m, NetSettlement = 400m, Status = FinalSettlementStatus.Finalized });
            seed.FinalSettlementLines.AddRange(
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Reimbursement, "Reimbursement", sourceIds.Reimbursement, 100m, true, 1),
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Recovery, "LoanRecovery", sourceIds.Loan, 100m, false, 2),
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Bonus, "VariablePay", sourceIds.VariablePay, 100m, true, 3),
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Recovery, "PayrollAdjustment", sourceIds.Adjustment, 100m, false, 4));
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == "MissingFinalSettlementReimbursement");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "MissingFinalSettlementLoan");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "MissingFinalSettlementVariablePay");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "MissingFinalSettlementAdjustment");
    }

    [Fact]
    public async Task Final_settlement_duplicate_category_sources_are_detected_without_mutating_sources()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        var sourceIds = await SeedFinalSettlementSources(database, ids, useMissingSourceIds: false);
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            var settlementId = Guid.NewGuid();
            seed.FinalSettlementCases.Add(new FinalSettlementCase { Id = settlementId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, PayrollRunId = ids.RunId, SeparationDate = new(2026, 9, 30), LastWorkingDate = new(2026, 9, 30), SettlementDate = new(2026, 10, 5), EmployeeCodeSnapshot = "SRC-EMP", EmployeeNameSnapshot = "Source Employee", GrossPayable = 400m, NetSettlement = 400m, Status = FinalSettlementStatus.Finalized });
            seed.FinalSettlementLines.AddRange(
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Reimbursement, "Reimbursement", sourceIds.Reimbursement, 100m, true, 1),
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Reimbursement, "Reimbursement", sourceIds.Reimbursement, 100m, true, 2),
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Recovery, "LoanRecovery", sourceIds.Loan, 100m, false, 3),
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Bonus, "VariablePay", sourceIds.VariablePay, 100m, true, 4),
                SettlementLine(settlementId, ids.TenantId, FinalSettlementLineType.Recovery, "PayrollAdjustment", sourceIds.Adjustment, 100m, false, 5));
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == "DuplicateFinalSettlementSource");
        await using var verify = database.CreateContext(new TestTenantContext(ids.TenantId));
        Assert.Equal(100m, await verify.ReimbursementSettlements.Where(x => x.PayrollRunId == ids.RunId).Select(x => x.Amount).SingleAsync());
    }

    [Fact]
    public async Task Missing_sources_and_final_settlement_mismatch_are_recorded_without_mutating_payroll()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: false, includeJournal: false);
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            var settlementId = Guid.NewGuid();
            seed.FinalSettlementCases.Add(new FinalSettlementCase { Id = settlementId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, PayrollRunId = ids.RunId, SeparationDate = new(2026, 9, 30), LastWorkingDate = new(2026, 9, 30), SettlementDate = new(2026, 10, 5), EmployeeCodeSnapshot = "SRC-EMP", EmployeeNameSnapshot = "Source Employee", GrossPayable = 9000m, NetSettlement = 9000m, Status = FinalSettlementStatus.Finalized });
            seed.FinalSettlementLines.Add(new FinalSettlementLine { Id = Guid.NewGuid(), TenantId = ids.TenantId, FinalSettlementCaseId = settlementId, LineType = FinalSettlementLineType.Gratuity, ComponentCode = "GRATUITY", Description = "Gratuity", Amount = 100m, IsEarning = true, SourceType = "Gratuity", SourceId = Guid.NewGuid(), Sequence = 1 });
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == "MissingBankAdvice");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "MissingPayrollJournal");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "FinalSettlementSourceMismatch");
        await using var verify = database.CreateContext(new TestTenantContext(ids.TenantId));
        var persisted = await verify.PayrollResults.SingleAsync(x => x.Id == ids.ResultId);
        Assert.Equal(10000m, persisted.GrossEarnings);
        Assert.Equal(9000m, persisted.NetPay);
    }

    [Fact]
    public async Task Exact_bank_and_accounting_sources_pass_without_source_mismatch_findings()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.DoesNotContain(result.Value!.Findings, x => x.ControlCode is "MissingBankAdvice" or "BankAdviceAmountMismatch" or "BankAdviceCountMismatch" or "DuplicateBankPaymentReference");
        Assert.DoesNotContain(result.Value.Findings, x => x.ControlCode is "MissingPayrollJournal" or "AccountingUnbalanced" or "DuplicatePayrollJournal");
    }

    [Fact]
    public async Task Post_payroll_reconciliation_records_bank_and_accounting_source_mismatches_without_mutating_sources()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var journalConfigId = Guid.NewGuid();
        var accountingConfigId = Guid.NewGuid();
        await using (var setup = database.CreateContext(new TestTenantContext()))
        {
            setup.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"SRC{tenantId:N}"[..10], TenantName = "Source tests", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
            setup.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "SRC-2026-09", Name = "Source", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open });
            setup.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "SRC-RUN", Status = PayrollRunStatus.Calculated });
            setup.PayrollAccountingConfigurations.Add(new PayrollAccountingConfiguration { Id = accountingConfigId, TenantId = tenantId, Code = "SRC-CONFIG", Name = "Source config" });
            setup.PayrollAccountingConfigurationVersions.Add(new PayrollAccountingConfigurationVersion { Id = journalConfigId, TenantId = tenantId, PayrollAccountingConfigurationId = accountingConfigId, EffectiveFrom = new(2026, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active });
            setup.BankAdviceBatches.Add(new BankAdviceBatch { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runId, PayrollPeriodId = periodId, BatchNumber = "SRC-BANK", BatchDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), Status = BankAdviceStatus.Approved, TotalEmployees = 1, TotalAmount = 999m });
            setup.PayrollJournalBatches.Add(new PayrollJournalBatch { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runId, PayrollPeriodId = periodId, JournalNumber = "SRC-JOURNAL", JournalDate = new(2026, 9, 30), Status = PayrollJournalStatus.Posted, TotalDebit = 100m, TotalCredit = 90m, AccountingConfigurationVersionId = journalConfigId });
            await setup.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        var service = new PayrollAnalyticsService(db, new TestTenantContext(tenantId), TimeProvider.System);
        var result = await service.GenerateReconciliationAsync(runId, PayrollReconciliationType.PostPayroll);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == "BankAdviceAmountMismatch");
        Assert.Contains(result.Value.Findings, x => x.ControlCode == "AccountingUnbalanced");
        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        Assert.Equal(999m, await verify.BankAdviceBatches.Where(x => x.PayrollRunId == runId).Select(x => x.TotalAmount).SingleAsync());
        Assert.Equal(100m, await verify.PayrollJournalBatches.Where(x => x.PayrollRunId == runId).Select(x => x.TotalDebit).SingleAsync());
    }

    private static FinalSettlementLine SettlementLine(Guid settlementId, Guid tenantId, FinalSettlementLineType type, string sourceType, Guid sourceId, decimal amount, bool earning, int sequence) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, FinalSettlementCaseId = settlementId, LineType = type, ComponentCode = sourceType, Description = sourceType, Amount = amount, IsEarning = earning, IsDeduction = !earning, SourceType = sourceType, SourceId = sourceId, Sequence = sequence
    };

    private static async Task<(Guid Reimbursement, Guid Loan, Guid VariablePay, Guid Adjustment)> SeedFinalSettlementSources(SqliteInMemoryDatabase database, (Guid TenantId, Guid RunId, Guid EmployeeId, Guid ResultId) ids, bool useMissingSourceIds)
    {
        var reimbursementId = Guid.NewGuid(); var loanId = Guid.NewGuid(); var variablePayId = Guid.NewGuid(); var adjustmentId = Guid.NewGuid();
        if (useMissingSourceIds) return (reimbursementId, loanId, variablePayId, adjustmentId);
        var categoryId = Guid.NewGuid(); var claimId = Guid.NewGuid(); var claimLineId = Guid.NewGuid(); var productId = Guid.NewGuid(); var productVersionId = Guid.NewGuid(); var installmentId = Guid.NewGuid(); var planId = Guid.NewGuid(); var planVersionId = Guid.NewGuid();
        await using var seed = database.CreateContext(new TestTenantContext(ids.TenantId));
        seed.ReimbursementCategories.Add(new ReimbursementCategory { Id = categoryId, TenantId = ids.TenantId, Code = "FINAL-NEG", Name = "Final negative", CategoryType = ReimbursementCategoryType.Other });
        seed.ReimbursementClaims.Add(new ReimbursementClaim { Id = claimId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, ClaimNumber = "FINAL-NEG-CLAIM", ClaimDate = new(2026, 9, 10), TotalClaimedAmount = 100m, TotalEligibleAmount = 100m, TotalApprovedAmount = 100m, SettledAmount = 100m, Status = ReimbursementClaimStatus.Settled });
        seed.ReimbursementClaimLines.Add(new ReimbursementClaimLine { Id = claimLineId, TenantId = ids.TenantId, ReimbursementClaimId = claimId, ReimbursementCategoryId = categoryId, ExpenseDate = new(2026, 9, 10), Description = "Final negative reimbursement", ClaimedAmount = 100m, EligibleAmount = 100m, ApprovedAmount = 100m, NonTaxableAmount = 100m, Status = ReimbursementClaimLineStatus.Settled });
        seed.ReimbursementSettlements.Add(new ReimbursementSettlement { Id = reimbursementId, TenantId = ids.TenantId, ReimbursementClaimId = claimId, ReimbursementClaimLineId = claimLineId, EmployeeId = ids.EmployeeId, SettlementType = ReimbursementSettlementType.Payroll, Amount = 100m, NonTaxableAmount = 100m, SettlementDate = new(2026, 9, 30), PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId });
        seed.LoanProducts.Add(new LoanProduct { Id = productId, TenantId = ids.TenantId, Code = "FINAL-NEG-LOAN", Name = "Final negative loan", ProductType = LoanProductType.Loan });
        seed.LoanProductVersions.Add(new LoanProductVersion { Id = productVersionId, TenantId = ids.TenantId, LoanProductId = productId, EffectiveFrom = new(2026, 1, 1), MinAmount = 1m, MaxAmount = 100000m, MinTenureMonths = 1, MaxTenureMonths = 12, InterestMethod = LoanInterestMethod.None, Status = LoanProductVersionStatus.Active });
        var employeeLoanId = Guid.NewGuid();
        seed.EmployeeLoans.Add(new EmployeeLoan { Id = employeeLoanId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, LoanProductId = productId, LoanProductVersionId = productVersionId, LoanNumber = "FINAL-NEG-LOAN-1", RequestedAmount = 100m, ApprovedAmount = 100m, DisbursedAmount = 100m, RequestedTenureMonths = 1, ApprovedTenureMonths = 1, InterestMethod = LoanInterestMethod.None, Status = LoanStatus.Active, OutstandingPrincipal = 0m, OutstandingTotal = 0m, RequestedAtUtc = DateTime.UtcNow });
        seed.LoanInstallments.Add(new LoanInstallment { Id = installmentId, TenantId = ids.TenantId, EmployeeLoanId = employeeLoanId, InstallmentNumber = 1, DueDate = new(2026, 9, 30), PrincipalAmount = 100m, InstallmentAmount = 100m, ClosingPrincipal = 0m, Status = LoanInstallmentStatus.Recovered, PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, RecoveredAmount = 100m });
        seed.LoanRepayments.Add(new LoanRepayment { Id = loanId, TenantId = ids.TenantId, EmployeeLoanId = employeeLoanId, LoanInstallmentId = installmentId, Amount = 100m, PrincipalAmount = 100m, RepaymentType = LoanRepaymentType.Payroll, PaymentDate = new(2026, 9, 30), SourceType = "PayrollResult", PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, CreatedByUserId = Guid.Empty });
        seed.VariablePayPlans.Add(new VariablePayPlan { Id = planId, TenantId = ids.TenantId, Code = "FINAL-NEG-VP", Name = "Final negative variable pay", PlanType = VariablePayPlanType.OneTimeAward });
        seed.VariablePayPlanVersions.Add(new VariablePayPlanVersion { Id = planVersionId, TenantId = ids.TenantId, VariablePayPlanId = planId, EffectiveFrom = new(2026, 1, 1), Status = VariablePayPlanVersionStatus.Published, CalculationMethod = VariablePayCalculationMethod.FixedAmount, FixedAmount = 100m });
        var awardId = Guid.NewGuid();
        seed.VariablePayAwards.Add(new VariablePayAward { Id = awardId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, VariablePayPlanId = planId, VariablePayPlanVersionId = planVersionId, AwardNumber = "FINAL-NEG-VP-1", AwardPeriodFrom = new(2026, 9, 1), AwardPeriodTo = new(2026, 9, 30), EligibilityDate = new(2026, 9, 30), SalaryBasisAmount = 10000m, CalculatedAmount = 100m, ApprovedAmount = 100m, SettledAmount = 100m, Status = VariablePayAwardStatus.Approved });
        seed.VariablePaySettlements.Add(new VariablePaySettlement { Id = variablePayId, TenantId = ids.TenantId, VariablePayAwardId = awardId, EmployeeId = ids.EmployeeId, SettlementType = VariablePaySettlementType.Payroll, Amount = 100m, SettlementDate = new(2026, 9, 30), PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, CreatedAtUtc = DateTime.UtcNow });
        seed.PayrollAdjustments.Add(new PayrollAdjustment { Id = adjustmentId, TenantId = ids.TenantId, EmployeeId = ids.EmployeeId, SourceType = "FinalNegative", SourceId = Guid.NewGuid(), AdjustmentType = PayrollAdjustmentType.AdditionalEarning, ComponentCode = "FINAL-NEG-ADJ", Description = "Final negative adjustment", Amount = 100m, AdjustmentNumber = "FINAL-NEG-ADJ-1", EffectiveDate = new(2026, 9, 30), Status = PayrollAdjustmentStatus.Applied, AppliedAmount = 100m });
        var applicationId = Guid.NewGuid();
        seed.PayrollAdjustmentApplications.Add(new PayrollAdjustmentApplication { Id = applicationId, TenantId = ids.TenantId, PayrollAdjustmentId = adjustmentId, PayrollRunId = ids.RunId, PayrollResultId = ids.ResultId, AppliedAmount = 100m, AppliedDate = new(2026, 9, 30) });
        await seed.SaveChangesAsync();
        return (reimbursementId, loanId, variablePayId, applicationId);
    }

    private static async Task<(Guid TenantId, Guid RunId, Guid EmployeeId, Guid ResultId)> SeedRun(SqliteInMemoryDatabase database, bool includeBank, bool includeJournal)
    {
        var tenantId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var runId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var resultId = Guid.NewGuid(); var runEmployeeId = Guid.NewGuid(); var structureId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var assignmentId = Guid.NewGuid(); var accountingConfigId = Guid.NewGuid(); var accountingVersionId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"SRC{tenantId:N}"[..10], TenantName = "Source matrix", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "SRC-EMP", FirstName = "Source", LastName = "Employee", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
        db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "SRC-P", Name = "Source matrix", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open });
        db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "SRC-MATRIX", Status = PayrollRunStatus.Calculated });
        db.SalaryStructures.Add(new SalaryStructure { Id = structureId, TenantId = tenantId, Code = "SRC-S", Name = "Source structure", IsActive = true });
        db.SalaryStructureVersions.Add(new SalaryStructureVersion { Id = versionId, TenantId = tenantId, SalaryStructureId = structureId, EffectiveFrom = new(2025, 1, 1), IsActive = true });
        db.EmployeeSalaryAssignments.Add(new EmployeeSalaryAssignment { Id = assignmentId, TenantId = tenantId, EmployeeId = employeeId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EffectiveFrom = new(2025, 1, 1), MonthlyCtc = 10000m, CurrencyCode = "INR", Status = EmployeeSalaryAssignmentStatus.Active });
        db.PayrollRunEmployees.Add(new PayrollRunEmployee { Id = runEmployeeId, TenantId = tenantId, PayrollRunId = runId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, EmploymentSnapshotDate = new(2026, 9, 30), IsEligible = true, Status = PayrollRunEmployeeStatus.Eligible });
        db.PayrollResults.Add(new PayrollResult { Id = resultId, TenantId = tenantId, PayrollRunId = runId, PayrollRunEmployeeId = runEmployeeId, EmployeeId = employeeId, EmployeeSalaryAssignmentId = assignmentId, SalaryStructureId = structureId, SalaryStructureVersionId = versionId, CalculationAttemptId = Guid.NewGuid(), PeriodStartDate = new(2026, 9, 1), PeriodEndDate = new(2026, 9, 30), EmploymentSnapshotDate = new(2026, 9, 30), CalendarDays = 30, EligibleDays = 30, CurrencyCode = "INR", GrossEarnings = 10000m, TotalDeductions = 1000m, NetPay = 9000m, Status = PayrollResultStatus.Calculated, IsCurrent = true });
        if (includeBank)
        {
            var batchId = Guid.NewGuid();
            db.BankAdviceBatches.Add(new BankAdviceBatch { Id = batchId, TenantId = tenantId, PayrollRunId = runId, PayrollPeriodId = periodId, BatchNumber = "SRC-BANK", BatchDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), Status = BankAdviceStatus.Approved, TotalEmployees = 1, TotalAmount = 9000m });
            db.BankAdvicePayments.Add(new BankAdvicePayment { Id = Guid.NewGuid(), TenantId = tenantId, BankAdviceBatchId = batchId, PayrollResultId = resultId, EmployeeId = employeeId, EmployeeCode = "SRC-EMP", EmployeeName = "Source Employee", NetPay = 9000m, PaymentStatus = BankAdvicePaymentStatus.Pending, PaymentReference = "SRC-REF-1", Sequence = 1 });
        }
        if (includeJournal)
        {
            db.PayrollAccountingConfigurations.Add(new PayrollAccountingConfiguration { Id = accountingConfigId, TenantId = tenantId, Code = "SRC-CFG", Name = "Source configuration" });
            db.PayrollAccountingConfigurationVersions.Add(new PayrollAccountingConfigurationVersion { Id = accountingVersionId, TenantId = tenantId, PayrollAccountingConfigurationId = accountingConfigId, EffectiveFrom = new(2025, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active });
            db.PayrollJournalBatches.Add(new PayrollJournalBatch { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runId, PayrollPeriodId = periodId, JournalNumber = "SRC-JOURNAL", JournalDate = new(2026, 9, 30), Status = PayrollJournalStatus.Posted, TotalDebit = 9000m, TotalCredit = 9000m, AccountingConfigurationVersionId = accountingVersionId });
        }
        await db.SaveChangesAsync();
        return (tenantId, runId, employeeId, resultId);
    }

    private static PayrollResultComponent SourceComponent((Guid TenantId, Guid RunId, Guid EmployeeId, Guid ResultId) ids, string source, decimal amount, int sequence) => new()
    {
        Id = Guid.NewGuid(), TenantId = ids.TenantId, PayrollResultId = ids.ResultId, CalculationAttemptId = Guid.NewGuid(), ComponentCode = $"SRC-{source}", ComponentName = source, ComponentType = SalaryComponentType.Earning, CalculationType = SalaryStructureCalculationType.Manual, CalculatedAmount = amount, IsEarning = true, CalculationSource = source, CalculationSequence = sequence
    };

    private static async Task AssertMissingSource(string calculationSource, string expectedCode)
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedRun(database, includeBank: true, includeJournal: true);
        await using (var seed = database.CreateContext(new TestTenantContext(ids.TenantId)))
        {
            seed.PayrollResultComponents.Add(SourceComponent(ids, calculationSource, 100m, 20));
            await seed.SaveChangesAsync();
        }

        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAnalyticsService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateReconciliationAsync(ids.RunId, PayrollReconciliationType.PostPayroll);
        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Value!.Findings, x => x.ControlCode == expectedCode);
    }
}
