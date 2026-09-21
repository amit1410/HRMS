using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollFinalSettlementAccountingTests
{
    [Fact]
    public async Task Final_settlement_recovery_generates_balanced_traceable_journal_once()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, includeFinalMapping: true);
        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var service = new PayrollAccountingService(db, new TestTenantContext(ids.TenantId), TimeProvider.System);

        var first = await service.GenerateFinalSettlementAsync(ids.SettlementId);
        Assert.True(first.Succeeded, first.Message);
        Assert.Equal(100m, first.Value!.TotalDebit);
        Assert.Equal(100m, first.Value.TotalCredit);
        Assert.Equal(3, first.Value.Lines.Count);
        Assert.Contains(first.Value.Lines, x => x.Debit == 100m && x.SourceType == "FinalSettlementLoanRepayment" && x.SourceId == ids.RepaymentId);
        Assert.Contains(first.Value.Lines, x => x.Credit == 90m && x.Description.Contains("principal", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(first.Value.Lines, x => x.Credit == 10m && x.Description.Contains("interest", StringComparison.OrdinalIgnoreCase));

        var persisted = await db.PayrollJournalBatches.Include(x => x.Lines).ThenInclude(x => x.Sources).SingleAsync();
        Assert.Equal(ids.SettlementId, persisted.FinalSettlementCaseId);
        Assert.All(persisted.Lines, line => Assert.Contains(line.Sources, source => source.SourceType == "FinalSettlement" && source.SourceId == ids.SettlementId));
        var duplicate = await service.GenerateFinalSettlementAsync(ids.SettlementId);
        Assert.Equal(ResultStatus.Conflict, duplicate.Status);
        Assert.Single(await db.PayrollJournalBatches.ToListAsync());
    }

    [Fact]
    public async Task Final_settlement_accounting_fails_without_receivable_mapping_without_side_effects()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, includeFinalMapping: false);
        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var service = new PayrollAccountingService(db, new TestTenantContext(ids.TenantId), TimeProvider.System);

        var result = await service.GenerateFinalSettlementAsync(ids.SettlementId);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("MissingGLMapping", result.Message, StringComparison.Ordinal);
        Assert.Empty(await db.PayrollJournalBatches.ToListAsync());
        Assert.Empty(await db.PayrollJournalLines.ToListAsync());
        var repayment = await db.LoanRepayments.SingleAsync();
        Assert.Equal(100m, repayment.Amount);
    }

    [Fact]
    public async Task Final_settlement_accounting_does_not_resolve_another_tenant_mapping()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, includeFinalMapping: false);
        var otherTenant = Guid.NewGuid();
        await AddTenantAsync(database, otherTenant);
        await using (var other = database.CreateContext(new TestTenantContext(otherTenant)))
        {
            var account = new PayrollGLAccount { Id = Guid.NewGuid(), TenantId = otherTenant, Code = "1300", Name = "Other receivable", AccountType = PayrollGLAccountType.Asset };
            var debit = new PayrollGLAccount { Id = Guid.NewGuid(), TenantId = otherTenant, Code = "2200", Name = "Other clearing", AccountType = PayrollGLAccountType.Liability };
            var configuration = new PayrollAccountingConfiguration { Id = Guid.NewGuid(), TenantId = otherTenant, Code = "OTHER", Name = "Other" };
            var version = new PayrollAccountingConfigurationVersion { Id = Guid.NewGuid(), TenantId = otherTenant, PayrollAccountingConfigurationId = configuration.Id, EffectiveFrom = new DateOnly(2026, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active };
            version.Mappings.Add(new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = otherTenant, MappingType = PayrollGLMappingType.LoanFinalSettlementRecovery, DebitAccountId = debit.Id, CreditAccountId = account.Id, Priority = 1 });
            other.AddRange(account, debit, configuration, version);
            await other.SaveChangesAsync();
        }
        await using var db = database.CreateContext(new TestTenantContext(ids.TenantId));
        var result = await new PayrollAccountingService(db, new TestTenantContext(ids.TenantId), TimeProvider.System).GenerateFinalSettlementAsync(ids.SettlementId);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("MissingGLMapping", result.Message, StringComparison.Ordinal);
        Assert.Empty(await db.PayrollJournalBatches.ToListAsync());
    }

    private static async Task<Ids> SeedAsync(SqliteInMemoryDatabase database, bool includeFinalMapping)
    {
        var tenantId = Guid.NewGuid(); await AddTenantAsync(database, tenantId);
        var employeeId = Guid.NewGuid(); var productId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var loanId = Guid.NewGuid(); var settlementId = Guid.NewGuid(); var repaymentId = Guid.NewGuid();
        var clearingId = Guid.NewGuid(); var receivableId = Guid.NewGuid(); var interestId = Guid.NewGuid(); var configId = Guid.NewGuid(); var configVersionId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext(tenantId));
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "FSA-001", FirstName = "Final", LastName = "Settlement", Email = "fsa@test.local", DateOfJoining = new DateOnly(2025, 1, 1), Status = EmployeeStatus.Active });
        db.LoanProducts.Add(new LoanProduct { Id = productId, TenantId = tenantId, Code = "FSA", Name = "Final settlement loan", ProductType = LoanProductType.Loan, CurrencyCode = "INR" });
        db.LoanProductVersions.Add(new LoanProductVersion { Id = versionId, TenantId = tenantId, LoanProductId = productId, EffectiveFrom = new DateOnly(2026, 1, 1), MinAmount = 1, MaxAmount = 10000, MinTenureMonths = 1, MaxTenureMonths = 12, Status = LoanProductVersionStatus.Active });
        db.EmployeeLoans.Add(new EmployeeLoan { Id = loanId, TenantId = tenantId, EmployeeId = employeeId, LoanProductId = productId, LoanProductVersionId = versionId, LoanNumber = "LN/FSA/001", RequestedAmount = 1000, ApprovedAmount = 1000, Status = LoanStatus.Active, OutstandingPrincipal = 90, OutstandingInterest = 10, OutstandingTotal = 100, CurrencyCode = "INR", RequestedAtUtc = DateTime.UtcNow });
        db.FinalSettlementCases.Add(new FinalSettlementCase { Id = settlementId, TenantId = tenantId, EmployeeId = employeeId, SeparationDate = new DateOnly(2026, 9, 30), LastWorkingDate = new DateOnly(2026, 9, 30), SettlementDate = new DateOnly(2026, 10, 5), Status = FinalSettlementStatus.Finalized, CurrencyCode = "INR" });
        db.LoanRepayments.Add(new LoanRepayment { Id = repaymentId, TenantId = tenantId, EmployeeLoanId = loanId, Amount = 100, PrincipalAmount = 90, InterestAmount = 10, RepaymentType = LoanRepaymentType.FinalSettlement, PaymentDate = new DateOnly(2026, 10, 5), SourceType = "FinalSettlement", Reference = settlementId.ToString(), CreatedByUserId = Guid.Empty });
        db.PayrollGLAccounts.AddRange(new PayrollGLAccount { Id = clearingId, TenantId = tenantId, Code = "2200", Name = "Settlement clearing", AccountType = PayrollGLAccountType.Liability }, new PayrollGLAccount { Id = receivableId, TenantId = tenantId, Code = "1300", Name = "Loan receivable", AccountType = PayrollGLAccountType.Asset }, new PayrollGLAccount { Id = interestId, TenantId = tenantId, Code = "4300", Name = "Loan interest", AccountType = PayrollGLAccountType.Liability });
        var configuration = new PayrollAccountingConfiguration { Id = configId, TenantId = tenantId, Code = "FSA-CFG", Name = "Final settlement accounting" };
        var configVersion = new PayrollAccountingConfigurationVersion { Id = configVersionId, TenantId = tenantId, PayrollAccountingConfigurationId = configId, EffectiveFrom = new DateOnly(2026, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active };
        if (includeFinalMapping) { configVersion.Mappings.Add(new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, MappingType = PayrollGLMappingType.LoanFinalSettlementRecovery, DebitAccountId = clearingId, CreditAccountId = receivableId, Priority = 1 }); configVersion.Mappings.Add(new PayrollGLMapping { Id = Guid.NewGuid(), TenantId = tenantId, MappingType = PayrollGLMappingType.LoanInterestRecovery, CreditAccountId = interestId, Priority = 1 }); }
        db.PayrollAccountingConfigurations.Add(configuration); db.PayrollAccountingConfigurationVersions.Add(configVersion); await db.SaveChangesAsync();
        return new(tenantId, settlementId, repaymentId);
    }

    private static async Task AddTenantAsync(SqliteInMemoryDatabase database, Guid tenantId)
    { await using var db = database.CreateContext(new TestTenantContext()); db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..12], TenantName = "Accounting tenant", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") }); await db.SaveChangesAsync(); }
    private sealed record Ids(Guid TenantId, Guid SettlementId, Guid RepaymentId);
}
