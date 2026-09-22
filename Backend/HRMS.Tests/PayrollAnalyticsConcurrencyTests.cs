using HRMS.Application.Services;
using HRMS.Application.DTOs.Payroll;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class PayrollAnalyticsConcurrencyTests
{
    [Fact]
    public async Task Concurrent_generation_preserves_one_current_version_and_prior_evidence()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        await SeedAsync(database, tenantId, runId);

        async Task<bool> GenerateAsync()
        {
            await using var db = database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid()));
            try
            {
                var result = await new PayrollAnalyticsService(db, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System)
                    .GenerateReconciliationAsync(runId, PayrollReconciliationType.PostPayroll);
                return result.Succeeded;
            }
            catch (DbUpdateException)
            {
                return false;
            }
        }

        var outcomes = await Task.WhenAll(GenerateAsync(), GenerateAsync());
        Assert.True(outcomes.Any(x => x));

        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        var rows = await verify.PayrollReconciliations.Where(x => x.PayrollRunId == runId && x.ReconciliationType == PayrollReconciliationType.PostPayroll).ToListAsync();
        Assert.InRange(rows.Count, 1, 2);
        Assert.Equal(rows.Count, await verify.PayrollAnalyticsSnapshots.CountAsync(x => x.PayrollRunId == runId && x.SnapshotType == PayrollAnalyticsSnapshotType.PostPayroll));
        Assert.Equal(rows.Max(x => x.Version), (await verify.PayrollReconciliations.Where(x => x.PayrollRunId == runId).MaxAsync(x => x.Version)));
    }

    [Fact]
    public async Task Concurrent_acknowledgement_and_resolution_leave_an_auditable_terminal_state()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        await SeedAsync(database, tenantId, runId);
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId)))
        {
            var service = new PayrollAnalyticsService(setup, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System);
            var generated = await service.GenerateReconciliationAsync(runId, PayrollReconciliationType.PrePayroll);
            Assert.True(generated.Succeeded, generated.Message);
            var finding = await setup.PayrollReconciliationFindings.SingleAsync();
            async Task<bool> ActAsync(string action)
            {
                await using var db = database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid()));
                try { return (await new PayrollAnalyticsService(db, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System).ActOnFindingAsync(finding.Id, action, new PayrollFindingActionRequest(null, null))).Succeeded; }
                catch (DbUpdateConcurrencyException) { return false; }
            }

            var outcomes = await Task.WhenAll(ActAsync("acknowledge"), ActAsync("resolve"));
            Assert.Contains(true, outcomes);
        }

        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        var final = await verify.PayrollReconciliationFindings.SingleAsync();
        Assert.Contains(final.Status, new[] { PayrollFindingStatus.Acknowledged, PayrollFindingStatus.Resolved });
    }

    [Fact]
    public async Task Concurrent_acknowledgements_preserve_one_auditable_terminal_state()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var runId = Guid.NewGuid(); await SeedAsync(database, tenantId, runId);
        Guid findingId;
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId)))
        {
            var generated = await new PayrollAnalyticsService(setup, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System).GenerateReconciliationAsync(runId, PayrollReconciliationType.PrePayroll);
            Assert.True(generated.Succeeded, generated.Message); findingId = await setup.PayrollReconciliationFindings.Select(x => x.Id).SingleAsync();
        }
        async Task<bool> AcknowledgeAsync(string reference)
        {
            await using var db = database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid()));
            try { return (await new PayrollAnalyticsService(db, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System).ActOnFindingAsync(findingId, "acknowledge", new PayrollFindingActionRequest("reviewed", reference))).Succeeded; }
            catch (DbUpdateConcurrencyException) { return false; }
        }
        var outcomes = await Task.WhenAll(AcknowledgeAsync("ACK-A"), AcknowledgeAsync("ACK-B"));
        Assert.Contains(true, outcomes);
        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        var finding = await verify.PayrollReconciliationFindings.SingleAsync(x => x.Id == findingId);
        Assert.Equal(PayrollFindingStatus.Acknowledged, finding.Status); Assert.NotNull(finding.AcknowledgedAtUtc); Assert.NotNull(finding.AcknowledgedByUserId);
    }

    [Fact]
    public async Task Regeneration_and_acknowledgement_preserve_prior_evidence()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var runId = Guid.NewGuid(); await SeedAsync(database, tenantId, runId);
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId)))
        {
            var service = new PayrollAnalyticsService(setup, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System);
            var first = await service.GenerateReconciliationAsync(runId, PayrollReconciliationType.PrePayroll); Assert.True(first.Succeeded, first.Message);
            var finding = await setup.PayrollReconciliationFindings.SingleAsync();
            var acknowledge = service.ActOnFindingAsync(finding.Id, "acknowledge", new PayrollFindingActionRequest("reviewed", "CONC-ACK"));
            var regenerate = new PayrollAnalyticsService(database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid())), new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System).GenerateReconciliationAsync(runId, PayrollReconciliationType.PrePayroll);
            await Task.WhenAll(acknowledge, regenerate);
        }
        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        Assert.Equal(2, await verify.PayrollReconciliations.CountAsync(x => x.PayrollRunId == runId));
        Assert.Contains(await verify.PayrollReconciliationFindings.Select(x => x.Status).ToListAsync(), x => x == PayrollFindingStatus.Acknowledged);
    }

    [Fact]
    public async Task Control_update_and_generation_capture_a_complete_control_version()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var runId = Guid.NewGuid(); await SeedAsync(database, tenantId, runId);
        await using var setup = database.CreateContext(new TestTenantContext(tenantId));
        var create = await new PayrollAnalyticsService(setup, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System).CreateControlAsync(new PayrollAnalyticsControlRequest { Code = "CONC_THRESHOLD", Name = "Concurrent threshold", Scope = PayrollControlScope.PayrollRun, Metric = PayrollControlMetric.NetPay, AbsoluteThreshold = 10m, EffectiveFrom = new(2026, 1, 1) });
        Assert.True(create.Succeeded, create.Message);
        var update = new PayrollAnalyticsService(database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid())), new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System).UpdateControlAsync(create.Value!.Id, new PayrollAnalyticsControlRequest { Code = "CONC_THRESHOLD", Name = "Updated", Scope = PayrollControlScope.PayrollRun, Metric = PayrollControlMetric.NetPay, AbsoluteThreshold = 20m, EffectiveFrom = new(2026, 1, 1) });
        var generate = new PayrollAnalyticsService(database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid())), new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System).GenerateReconciliationAsync(runId, PayrollReconciliationType.PrePayroll);
        await Task.WhenAll(update, generate);
        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        Assert.Single(await verify.PayrollVarianceControls.ToListAsync());
        Assert.Single(await verify.PayrollReconciliations.ToListAsync());
    }

    [Fact]
    public async Task Resolution_and_regeneration_retain_resolution_history()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var runId = Guid.NewGuid(); await SeedAsync(database, tenantId, runId);
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId)))
        {
            var service = new PayrollAnalyticsService(setup, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System);
            var first = await service.GenerateReconciliationAsync(runId, PayrollReconciliationType.PrePayroll); var finding = await setup.PayrollReconciliationFindings.SingleAsync();
            var resolve = service.ActOnFindingAsync(finding.Id, "resolve", new PayrollFindingActionRequest("fixed in source", "SRC-1"));
            var regenerate = new PayrollAnalyticsService(database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid())), new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System).GenerateReconciliationAsync(runId, PayrollReconciliationType.PrePayroll);
            await Task.WhenAll(resolve, regenerate);
        }
        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        var findingAfter = await verify.PayrollReconciliationFindings.ToListAsync();
        Assert.Contains(findingAfter, x => x.Status == PayrollFindingStatus.Resolved && x.ResolutionReference == "SRC-1");
        Assert.Equal(2, await verify.PayrollReconciliations.CountAsync());
    }

    [Fact]
    public async Task Accounting_posting_and_post_payroll_reconciliation_observe_consistent_committed_state()
    {
        using var database = new SqliteInMemoryDatabase(); var tenantId = Guid.NewGuid(); var runId = Guid.NewGuid(); await SeedAsync(database, tenantId, runId);
        var configId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        await using (var setup = database.CreateContext(new TestTenantContext(tenantId)))
        {
            setup.PayrollAccountingConfigurations.Add(new PayrollAccountingConfiguration { Id = configId, TenantId = tenantId, Code = "CONC-ACCOUNTING", Name = "Concurrency accounting" });
            setup.PayrollAccountingConfigurationVersions.Add(new PayrollAccountingConfigurationVersion { Id = versionId, TenantId = tenantId, PayrollAccountingConfigurationId = configId, EffectiveFrom = new(2026, 1, 1), Status = PayrollAccountingConfigurationVersionStatus.Active });
            await setup.SaveChangesAsync();
        }
        async Task<bool> PostAsync()
        {
            await using var db = database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid()));
            db.PayrollJournalBatches.Add(new PayrollJournalBatch { Id = Guid.NewGuid(), TenantId = tenantId, PayrollRunId = runId, JournalNumber = "CONC-POST", JournalDate = new(2026, 9, 30), Status = PayrollJournalStatus.Posted, TotalDebit = 0m, TotalCredit = 0m, AccountingConfigurationVersionId = versionId });
            try { await db.SaveChangesAsync(); return true; } catch (DbUpdateException) { return false; }
        }
        async Task<bool> ReconcileAsync()
        {
            await using var db = database.CreateContext(new TestTenantContext(tenantId, Guid.NewGuid()));
            try { return (await new PayrollAnalyticsService(db, new TestTenantContext(tenantId, Guid.NewGuid()), TimeProvider.System).GenerateReconciliationAsync(runId, PayrollReconciliationType.PostPayroll)).Succeeded; } catch (DbUpdateException) { return false; }
        }
        var outcomes = await Task.WhenAll(PostAsync(), ReconcileAsync());
        Assert.Contains(true, outcomes);
        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        Assert.Equal(1, await verify.PayrollJournalBatches.CountAsync(x => x.PayrollRunId == runId));
        Assert.True(await verify.PayrollReconciliations.AnyAsync(x => x.PayrollRunId == runId));
        Assert.Equal(0m, await verify.PayrollJournalBatches.Where(x => x.PayrollRunId == runId).Select(x => x.TotalDebit).SingleAsync());
    }

    private static async Task SeedAsync(SqliteInMemoryDatabase database, Guid tenantId, Guid runId)
    {
        var periodId = Guid.NewGuid();
        await using var db = database.CreateContext(new TestTenantContext());
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"PA{tenantId:N}"[..10], TenantName = "Analytics concurrency", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
        db.PayrollPeriods.Add(new PayrollPeriod { Id = periodId, TenantId = tenantId, Code = "AN-CONCURRENCY", Name = "Analytics concurrency", PeriodType = PayrollPeriodType.Monthly, StartDate = new(2026, 9, 1), EndDate = new(2026, 9, 30), PayDate = new(2026, 10, 5), FiscalYear = 2026, PeriodNumber = 9, Status = PayrollPeriodStatus.Open });
        db.PayrollRuns.Add(new PayrollRun { Id = runId, TenantId = tenantId, PayrollPeriodId = periodId, RunNumber = "AN-CONCURRENCY-RUN", Status = PayrollRunStatus.Calculated });
        await db.SaveChangesAsync();
    }
}
