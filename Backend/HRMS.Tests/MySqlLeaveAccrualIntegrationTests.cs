using HRMS.Application.Services;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using HRMS.Infrastructure.Security;
using HRMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlLeaveAccrualIntegrationTests
{
    [Fact]
    public async Task MySql_daily_accrual_processes_three_dates_and_is_idempotent()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL accrual test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var employee = await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId);
                employee.DateOfJoining = new DateOnly(2026, 10, 1);
                var manager = await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId);
                manager.Status = EmployeeStatus.Terminated;
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.EntitlementQuantity = 0.25m;
                rule.AccrualFrequency = AccrualFrequency.Daily;
                await setup.SaveChangesAsync();
                var employment = new EffectiveEmploymentResolver(setup, fixture.EmployeeTenant);
                var policy = await new LeavePolicyResolver(setup, employment, fixture.EmployeeTenant).ResolveAsync(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, new DateOnly(2026, 10, 1));
                Assert.Equal(LeavePolicyResolutionStatus.Resolved, policy.Status);
                var period = await new LeavePeriodResolver(setup, fixture.EmployeeTenant).ResolveAsync(fixture.TenantId, new DateOnly(2026, 10, 1));
                Assert.Equal(LeavePeriodResolutionStatus.Resolved, period.Status);
                Assert.Equal(AccrualFrequency.Daily, await setup.LeavePolicyEntitlementRules.Where(x => x.LeavePolicyRuleId == fixture.PolicyRuleId).Select(x => x.AccrualFrequency).SingleAsync());
                Assert.Equal(EntitlementSource.PolicyAccrual, await setup.LeavePolicyEntitlementRules.Where(x => x.LeavePolicyRuleId == fixture.PolicyRuleId).Select(x => x.EntitlementSource).SingleAsync());
                Assert.Equal(1, await setup.Employees.CountAsync(x => x.Id == fixture.EmployeeId && x.Status == EmployeeStatus.Active && x.DateOfJoining <= new DateOnly(2026, 10, 3)));
            }

            await using (var first = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var processor = CreateProcessor(first, fixture);
                var result = await processor.ProcessAsync(new DateOnly(2026, 10, 3));
                Assert.True(result.Processed == 3, $"Processed={result.Processed}; Skipped={result.Skipped}; Failed={result.Failed}; Credited={result.CreditedQuantity}");
                Assert.Equal(0.75m, result.CreditedQuantity);
            }

            await using (var verification = fixture.CreateContext(fixture.EmployeeTenant))
            {
                Assert.Equal(3, await verification.LeaveAccrualOccurrences.CountAsync(x => x.TenantId == fixture.TenantId && x.Status == "Processed"));
                Assert.Equal(3, await verification.LeaveBalanceTransactions.CountAsync(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.Accrual));
                Assert.Equal(3, await verification.LeaveEntitlementGrants.CountAsync(x => x.TenantId == fixture.TenantId && x.SourceType == LeaveBalanceSourceType.Policy));
                Assert.Equal(10.75m, await verification.EmployeeLeaveBalances.Where(x => x.Id == fixture.BalanceId).Select(x => x.GrantedQuantity).SingleAsync());
            }

            await using (var second = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var processor = CreateProcessor(second, fixture);
                var result = await processor.ProcessAsync(new DateOnly(2026, 10, 3));
                Assert.Equal(0, result.Processed);
                Assert.Equal(0m, result.CreditedQuantity);
            }

            await using var finalState = fixture.CreateContext(fixture.EmployeeTenant);
            Assert.Equal(3, await finalState.LeaveAccrualOccurrences.CountAsync(x => x.TenantId == fixture.TenantId && x.Status == "Processed"));
            Assert.Equal(3, await finalState.LeaveBalanceTransactions.CountAsync(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.Accrual));
            Assert.Equal(3, await finalState.LeaveEntitlementGrants.CountAsync(x => x.TenantId == fixture.TenantId && x.SourceType == LeaveBalanceSourceType.Policy));
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_monthly_accrual_is_one_occurrence_per_month()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL accrual test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var employee = await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId);
                employee.DateOfJoining = new DateOnly(2026, 10, 1);
                var manager = await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId);
                manager.Status = EmployeeStatus.Terminated;
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.EntitlementQuantity = 1.5m;
                rule.AccrualFrequency = AccrualFrequency.Monthly;
                rule.AccrualTiming = AccrualTiming.EndOfPeriod;
                await setup.SaveChangesAsync();
            }

            await using (var october = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var result = await CreateProcessor(october, fixture).ProcessAsync(new DateOnly(2026, 10, 31));
                Assert.Equal(1, result.Processed);
                Assert.Equal(1.5m, result.CreditedQuantity);
            }
            await using (var november = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var result = await CreateProcessor(november, fixture).ProcessAsync(new DateOnly(2026, 11, 30));
                Assert.Equal(1, result.Processed);
                Assert.Equal(1.5m, result.CreditedQuantity);
            }

            await using var verification = fixture.CreateContext(fixture.EmployeeTenant);
            Assert.Equal(2, await verification.LeaveAccrualOccurrences.CountAsync(x => x.TenantId == fixture.TenantId && x.Status == "Processed"));
            Assert.Equal(3m, await verification.LeaveBalanceTransactions.Where(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.Accrual).SumAsync(x => x.Quantity));
            Assert.Equal(3m, await verification.LeaveEntitlementGrants.Where(x => x.TenantId == fixture.TenantId && x.SourceType == LeaveBalanceSourceType.Policy).SumAsync(x => x.GrantedQuantity));
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_carry_forward_expiry_debits_only_the_unconsumed_grant_quantity()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL grant expiry test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            Guid grantId;
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var poster = new LeaveBalanceTransactionPoster(setup, fixture.EmployeeTenant, TimeProvider.System);
                var credit = await poster.PostCreditAsync(new(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, fixture.LeavePeriodId, LeaveBalanceTransactionType.CarryForward, 10m, new(2026, 1, 1), null, null, LeaveBalanceSourceType.CarryForward, "mysql-carry-expiry", LeaveBalanceActorType.System, null, null, "mysql-carry-expiry-credit", null, new(2026, 10, 2)));
                Assert.True(credit.Succeeded, credit.Message);
                grantId = await setup.LeaveEntitlementGrants.Where(x => x.SourceReference == "mysql-carry-expiry").Select(x => x.Id).SingleAsync();

                var employment = new EffectiveEmploymentResolver(setup, fixture.EmployeeTenant);
                var validation = new LeaveRequestValidationService(setup, new EmployeeIdentityResolver(setup, fixture.EmployeeTenant), employment, new LeavePeriodResolver(setup, fixture.EmployeeTenant), new LeavePolicyResolver(setup, employment, fixture.EmployeeTenant));
                var submission = new LeaveRequestSubmissionService(setup, new EmployeeIdentityResolver(setup, fixture.EmployeeTenant), validation, new MySqlLeaveRequestSubmissionLock(setup), TimeProvider.System, balanceAccountingService: new LeaveBalanceAccountingService(setup, fixture.EmployeeTenant, TimeProvider.System));
                var submitted = await submission.SubmitAsync(new(fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate.AddDays(3), "mysql-carry-expiry-request"));
                Assert.True(submitted.Succeeded, submitted.Message);
                var approval = new LeaveRequestApprovalService(setup, new EmployeeIdentityResolver(setup, fixture.ManagerTenant), new EmployeeManagerResolver(setup, fixture.ManagerTenant), new MySqlLeaveRequestSubmissionLock(setup), TimeProvider.System, balanceAccountingService: new LeaveBalanceAccountingService(setup, fixture.ManagerTenant, TimeProvider.System));
                var approved = await approval.ApproveAsync(submitted.Value!.RequestId);
                Assert.True(approved.Succeeded, approved.Message);
            }

            await using (var expiryContext = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var processor = new LeaveEntitlementExpiryProcessor(expiryContext, fixture.EmployeeTenant, new LeaveBalanceTransactionPoster(expiryContext, fixture.EmployeeTenant, TimeProvider.System));
                var result = await processor.ProcessAsync(new(2026, 10, 2));
                Assert.Equal(1, result.Processed);
                Assert.Equal(6m, result.ExpiredQuantity);
                var grant = await expiryContext.LeaveEntitlementGrants.AsNoTracking().SingleAsync(x => x.Id == grantId);
                Assert.Equal(4m, grant.ConsumedQuantity);
                Assert.Equal(6m, grant.ExpiredQuantity);
                Assert.Equal(0m, grant.AvailableQuantity);
                Assert.Equal(1, await expiryContext.LeaveBalanceTransactions.CountAsync(x => x.IdempotencyKey == $"leave-grant-expiry:{grantId:D}:2026-10-02"));
            }

            await using var replayContext = fixture.CreateContext(fixture.EmployeeTenant);
            var replay = await new LeaveEntitlementExpiryProcessor(replayContext, fixture.EmployeeTenant, new LeaveBalanceTransactionPoster(replayContext, fixture.EmployeeTenant, TimeProvider.System)).ProcessAsync(new(2026, 10, 2));
            Assert.Equal(0, replay.Processed);
            Assert.Equal(1, await replayContext.LeaveBalanceTransactions.CountAsync(x => x.IdempotencyKey == $"leave-grant-expiry:{grantId:D}:2026-10-02"));
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_two_accrual_processors_credit_one_occurrence()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL accrual concurrency test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                (await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId)).DateOfJoining = new DateOnly(2026, 10, 3);
                (await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId)).Status = EmployeeStatus.Terminated;
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.EntitlementQuantity = 0.25m;
                rule.AccrualFrequency = AccrualFrequency.Daily;
                await setup.SaveChangesAsync();
            }

            await using var contextA = fixture.CreateContext(fixture.EmployeeTenant);
            await using var contextB = fixture.CreateContext(fixture.EmployeeTenant);
            var results = await Task.WhenAll(
                CreateProcessor(contextA, fixture).ProcessAsync(new DateOnly(2026, 10, 3)),
                CreateProcessor(contextB, fixture).ProcessAsync(new DateOnly(2026, 10, 3)));
            Assert.Equal(1, results.Count(x => x.Processed == 1));
            Assert.Equal(1, results.Count(x => x.Processed == 0));

            await using var verification = fixture.CreateContext(fixture.EmployeeTenant);
            Assert.Equal(1, await verification.LeaveAccrualOccurrences.CountAsync(x => x.TenantId == fixture.TenantId && x.Status == "Processed"));
            Assert.Equal(1, await verification.LeaveBalanceTransactions.CountAsync(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.Accrual));
            Assert.Equal(1, await verification.LeaveEntitlementGrants.CountAsync(x => x.TenantId == fixture.TenantId && x.SourceType == LeaveBalanceSourceType.Policy));
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_yearly_accrual_uses_the_configured_leave_period_once()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL yearly accrual test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                (await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId)).DateOfJoining = new DateOnly(2026, 10, 1);
                (await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId)).Status = EmployeeStatus.Terminated;
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.EntitlementQuantity = 18m;
                rule.AccrualFrequency = AccrualFrequency.Yearly;
                rule.AccrualTiming = AccrualTiming.EndOfPeriod;
                await setup.SaveChangesAsync();
            }
            await using (var process = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var result = await CreateProcessor(process, fixture).ProcessAsync(new DateOnly(2026, 12, 31));
                Assert.Equal(1, result.Processed);
                Assert.Equal(18m, result.CreditedQuantity);
            }
            await using var verification = fixture.CreateContext(fixture.EmployeeTenant);
            var occurrence = await verification.LeaveAccrualOccurrences.SingleAsync(x => x.TenantId == fixture.TenantId && x.AccrualFrequency == AccrualFrequency.Yearly);
            Assert.Equal(fixture.LeavePeriodId, occurrence.LeavePeriodId);
            Assert.Equal(18m, await verification.LeaveBalanceTransactions.Where(x => x.IdempotencyKey == $"leave-accrual:{occurrence.OccurrenceKey}").Select(x => x.Quantity).SingleAsync());
            Assert.Equal(18m, await verification.LeaveEntitlementGrants.Where(x => x.SourceReference == occurrence.OccurrenceKey).Select(x => x.GrantedQuantity).SingleAsync());
            await using var rerun = fixture.CreateContext(fixture.EmployeeTenant);
            var replay = await CreateProcessor(rerun, fixture).ProcessAsync(new DateOnly(2026, 12, 31));
            Assert.Equal(0, replay.Processed);
            Assert.Equal(1, await rerun.LeaveAccrualOccurrences.CountAsync(x => x.TenantId == fixture.TenantId && x.AccrualFrequency == AccrualFrequency.Yearly));
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_monthly_proration_persists_the_production_calculation()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL proration test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                (await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId)).DateOfJoining = new DateOnly(2026, 10, 16);
                (await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId)).Status = EmployeeStatus.Terminated;
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.EntitlementQuantity = 1.5m;
                rule.AccrualFrequency = AccrualFrequency.Monthly;
                rule.AccrualTiming = AccrualTiming.EndOfPeriod;
                rule.ProratePartialPeriod = true;
                await setup.SaveChangesAsync();
            }
            await using var process = fixture.CreateContext(fixture.EmployeeTenant);
            var result = await CreateProcessor(process, fixture).ProcessAsync(new DateOnly(2026, 10, 31));
            Assert.Equal(1, result.Processed);
            var occurrence = await process.LeaveAccrualOccurrences.SingleAsync(x => x.TenantId == fixture.TenantId);
            Assert.Equal(16m / 31m * 1.5m, occurrence.CalculatedQuantity, 3);
            Assert.Equal(occurrence.CreditedQuantity, await process.LeaveBalanceTransactions.Where(x => x.TransactionType == LeaveBalanceTransactionType.Accrual).Select(x => x.Quantity).SingleAsync());
            Assert.Equal(occurrence.CreditedQuantity, await process.LeaveEntitlementGrants.Where(x => x.TenantId == fixture.TenantId).Select(x => x.GrantedQuantity).SingleAsync());
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_cap_posts_partial_credit_and_marks_zero_credit_occurrence_processed()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL cap test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                (await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId)).DateOfJoining = new DateOnly(2026, 10, 1);
                (await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId)).Status = EmployeeStatus.Terminated;
                var balance = await setup.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
                balance.GrantedQuantity = 29m;
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.EntitlementQuantity = 1.5m;
                rule.AccrualFrequency = AccrualFrequency.Daily;
                rule.MaximumAccumulation = 30m;
                await setup.SaveChangesAsync();
            }
            await using (var process = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var result = await CreateProcessor(process, fixture).ProcessAsync(new DateOnly(2026, 10, 1));
                Assert.Equal(1, result.Processed);
                Assert.Equal(1m, result.CreditedQuantity);
            }
            await using (var zeroSetup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var balance = await zeroSetup.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
                balance.GrantedQuantity = 30m;
                await zeroSetup.SaveChangesAsync();
            }
            await using (var zeroProcess = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var result = await CreateProcessor(zeroProcess, fixture).ProcessAsync(new DateOnly(2026, 10, 2));
                Assert.Equal(1, result.Processed);
                Assert.Equal(0m, result.CreditedQuantity);
                var occurrence = await zeroProcess.LeaveAccrualOccurrences.SingleAsync(x => x.OccurrenceDate == new DateOnly(2026, 10, 2));
                Assert.Equal("Skipped", occurrence.Status);
                Assert.Equal(0, await zeroProcess.LeaveBalanceTransactions.CountAsync(x => x.TransactionType == LeaveBalanceTransactionType.Accrual && x.EffectiveDate == new DateOnly(2026, 10, 2)));
                Assert.Equal(0, await zeroProcess.LeaveEntitlementGrants.CountAsync(x => x.GrantedOn == new DateOnly(2026, 10, 2)));
            }
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_two_period_close_processors_post_one_carry_and_one_lapse()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL period-close concurrency test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var destinationId = Guid.NewGuid();
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                setup.LeavePeriods.Add(new LeavePeriod { Id = destinationId, TenantId = fixture.TenantId, Code = $"NEXT{fixture.TenantId:N}"[..8], Name = "2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31), IsActive = true });
                (await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId)).Status = EmployeeStatus.Terminated;
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.CarryForwardEnabled = true;
                rule.MaximumCarryForwardQuantity = 5m;
                await setup.SaveChangesAsync();
                var balance = await setup.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
                balance.GrantedQuantity = 0m;
                await setup.SaveChangesAsync();
                var poster = new LeaveBalanceTransactionPoster(setup, fixture.EmployeeTenant, TimeProvider.System);
                var seedCredit = await poster.PostCreditAsync(new LeaveBalanceCreditCommand(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, fixture.LeavePeriodId, LeaveBalanceTransactionType.Opening, 10m, new(2026, 1, 1), null, null, LeaveBalanceSourceType.BalanceImport, "period-close-fixture", LeaveBalanceActorType.System, null, null, "period-close-fixture-credit", null));
                Assert.True(seedCredit.Succeeded, seedCredit.Message);
            }
            {
                await using var contextA = fixture.CreateContext(fixture.EmployeeTenant);
                await using var contextB = fixture.CreateContext(fixture.EmployeeTenant);
                var processorA = new LeavePeriodCloseProcessor(contextA, fixture.EmployeeTenant, new LeavePolicyResolver(contextA, new EffectiveEmploymentResolver(contextA, fixture.EmployeeTenant), fixture.EmployeeTenant), new LeaveBalanceTransactionPoster(contextA, fixture.EmployeeTenant, TimeProvider.System), TimeProvider.System);
                var processorB = new LeavePeriodCloseProcessor(contextB, fixture.EmployeeTenant, new LeavePolicyResolver(contextB, new EffectiveEmploymentResolver(contextB, fixture.EmployeeTenant), fixture.EmployeeTenant), new LeaveBalanceTransactionPoster(contextB, fixture.EmployeeTenant, TimeProvider.System), TimeProvider.System);
                var results = await Task.WhenAll(processorA.ProcessAsync(fixture.LeavePeriodId, new(2027, 1, 1)), processorB.ProcessAsync(fixture.LeavePeriodId, new(2027, 1, 1)));
                Assert.Equal(1, results.Count(x => x.Processed == 1));
                Assert.Equal(1, results.Count(x => x.Processed == 0));
            }

            await using (var verification = fixture.CreateContext(fixture.EmployeeTenant))
            {
                Assert.Equal(1, await verification.LeavePeriodCloseOccurrences.CountAsync(x => x.TenantId == fixture.TenantId && x.Status == "Processed"));
                Assert.Equal(1, await verification.LeaveBalanceTransactions.CountAsync(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.CarryForward));
                Assert.Equal(1, await verification.LeaveEntitlementGrants.CountAsync(x => x.TenantId == fixture.TenantId && x.SourceType == LeaveBalanceSourceType.CarryForward));
                Assert.Equal(5m, await verification.LeaveBalanceTransactions.Where(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.CarryForward).Select(x => x.Quantity).SingleAsync());
                Assert.Equal(5m, await verification.LeaveBalanceTransactions.Where(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.Expiry).Select(x => x.Quantity).SingleAsync());
                Assert.Equal(destinationId, await verification.LeaveEntitlementGrants.Where(x => x.SourceType == LeaveBalanceSourceType.CarryForward).Select(x => x.LeavePeriodId).SingleAsync());
            }

            await using (var restartContext = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var replay = await new LeavePeriodCloseProcessor(restartContext, fixture.EmployeeTenant, new LeavePolicyResolver(restartContext, new EffectiveEmploymentResolver(restartContext, fixture.EmployeeTenant), fixture.EmployeeTenant), new LeaveBalanceTransactionPoster(restartContext, fixture.EmployeeTenant, TimeProvider.System), TimeProvider.System).ProcessAsync(fixture.LeavePeriodId, new(2027, 1, 1));
                Assert.Equal(0, replay.Processed);
                Assert.Equal(1, await restartContext.LeaveBalanceTransactions.CountAsync(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.CarryForward));
                Assert.Equal(1, await restartContext.LeaveBalanceTransactions.CountAsync(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.Expiry));
            }
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_concurrent_reservations_cannot_overallocate_source_grants()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL reservation concurrency test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var balance = await setup.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
                balance.GrantedQuantity = 0m;
                await setup.SaveChangesAsync();
                var credit = await new LeaveBalanceTransactionPoster(setup, fixture.EmployeeTenant, TimeProvider.System).PostCreditAsync(
                    new(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, fixture.LeavePeriodId, LeaveBalanceTransactionType.Opening, 5m, new(2026, 1, 1), null, null, LeaveBalanceSourceType.BalanceImport, "reservation-race", LeaveBalanceActorType.System, null, null, "reservation-race-credit", null));
                Assert.True(credit.Succeeded, credit.Message);
            }

            await using var contextA = fixture.CreateContext(fixture.EmployeeTenant);
            await using var contextB = fixture.CreateContext(fixture.EmployeeTenant);
            var submitA = CreateSubmission(contextA, fixture);
            var submitB = CreateSubmission(contextB, fixture);
            var results = await Task.WhenAll(
                submitA.SubmitAsync(new(fixture.LeaveTypeId, new(2026, 10, 1), new(2026, 10, 4), "reservation-race-a")),
                submitB.SubmitAsync(new(fixture.LeaveTypeId, new(2026, 10, 5), new(2026, 10, 8), "reservation-race-b")));

            Assert.Equal(1, results.Count(x => x.Succeeded));
            Assert.Equal(1, results.Count(x => !x.Succeeded));

            await using var verification = fixture.CreateContext(fixture.EmployeeTenant);
            var grant = await verification.LeaveEntitlementGrants.SingleAsync(x => x.SourceReference == "reservation-race");
            Assert.True(grant.ReservedQuantity <= 5m);
            Assert.True(grant.AvailableQuantity >= 0m);
            Assert.Equal(4m, grant.ReservedQuantity);
            Assert.Equal(1, await verification.LeaveBalanceReservationAllocations.CountAsync(x => x.TenantId == fixture.TenantId));
            var balanceState = await verification.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
            Assert.True(balanceState.ReservedQuantity <= balanceState.GrantedQuantity);
            Assert.True(balanceState.AvailableQuantity >= 0m);
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_accrual_processing_is_isolated_between_tenants()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL tenant isolation test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var tenantA = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var tenantB = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await tenantA.SeedAsync();
            await tenantB.SeedAsync();
            await ConfigureDailyTenantAsync(tenantA);
            await ConfigureDailyTenantAsync(tenantB);

            await using (var contextA = tenantA.CreateContext(tenantA.EmployeeTenant))
            {
                var result = await CreateProcessor(contextA, tenantA).ProcessAsync(new(2026, 10, 3));
                Assert.Equal(3, result.Processed);
            }

            await using (var contextB = tenantB.CreateContext(tenantB.EmployeeTenant))
            {
                Assert.Equal(0, await contextB.LeaveAccrualOccurrences.CountAsync(x => x.TenantId == tenantB.TenantId));
                Assert.Equal(0, await contextB.LeaveBalanceTransactions.CountAsync(x => x.TenantId == tenantB.TenantId && x.TransactionType == LeaveBalanceTransactionType.Accrual));
                Assert.Equal(0, await contextB.LeaveEntitlementGrants.CountAsync(x => x.TenantId == tenantB.TenantId && x.SourceType == LeaveBalanceSourceType.Policy));
            }

            await using (var contextB = tenantB.CreateContext(tenantB.EmployeeTenant))
            {
                var result = await CreateProcessor(contextB, tenantB).ProcessAsync(new(2026, 10, 3));
                Assert.Equal(3, result.Processed);
            }
        }
        finally
        {
            await tenantB.CleanupAsync();
            await tenantA.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_yearly_proration_uses_leave_period_fraction()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL yearly proration test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                (await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId)).DateOfJoining = new DateOnly(2026, 10, 1);
                (await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId)).Status = EmployeeStatus.Terminated;
                var period = await setup.LeavePeriods.SingleAsync(x => x.Id == fixture.LeavePeriodId);
                period.StartDate = new DateOnly(2026, 4, 1);
                period.EndDate = new DateOnly(2027, 3, 31);
                var version = await setup.LeavePolicyVersions.SingleAsync(x => x.Id == fixture.PolicyVersionId);
                version.EffectiveFrom = period.StartDate;
                version.EffectiveTo = period.EndDate;
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.EntitlementQuantity = 18m;
                rule.AccrualFrequency = AccrualFrequency.Yearly;
                rule.AccrualTiming = AccrualTiming.EndOfPeriod;
                rule.ProratePartialPeriod = true;
                await setup.SaveChangesAsync();
            }

            await using var process = fixture.CreateContext(fixture.EmployeeTenant);
            var result = await CreateProcessor(process, fixture).ProcessAsync(new DateOnly(2027, 3, 31));
            var expected = 18m * (new DateOnly(2027, 3, 31).DayNumber - new DateOnly(2026, 10, 1).DayNumber + 1) / 365m;
            Assert.Equal(1, result.Processed);
            var occurrence = await process.LeaveAccrualOccurrences.SingleAsync(x => x.TenantId == fixture.TenantId);
            Assert.Equal(expected, occurrence.CalculatedQuantity, 3);
            Assert.Equal(expected, occurrence.CreditedQuantity, 3);
            Assert.Equal(expected, await process.LeaveBalanceTransactions.Where(x => x.TransactionType == LeaveBalanceTransactionType.Accrual).Select(x => x.Quantity).SingleAsync(), 3);
            Assert.Equal(expected, await process.LeaveEntitlementGrants.Where(x => x.SourceType == LeaveBalanceSourceType.Policy).Select(x => x.GrantedQuantity).SingleAsync(), 3);
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_yearly_proration_disabled_credits_full_occurrence()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL no-proration test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                (await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId)).DateOfJoining = new DateOnly(2026, 10, 1);
                (await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId)).Status = EmployeeStatus.Terminated;
                var period = await setup.LeavePeriods.SingleAsync(x => x.Id == fixture.LeavePeriodId);
                period.StartDate = new DateOnly(2026, 4, 1);
                period.EndDate = new DateOnly(2027, 3, 31);
                var version = await setup.LeavePolicyVersions.SingleAsync(x => x.Id == fixture.PolicyVersionId);
                version.EffectiveFrom = period.StartDate;
                version.EffectiveTo = period.EndDate;
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.EntitlementQuantity = 18m;
                rule.AccrualFrequency = AccrualFrequency.Yearly;
                rule.AccrualTiming = AccrualTiming.EndOfPeriod;
                rule.ProratePartialPeriod = false;
                await setup.SaveChangesAsync();
            }

            await using var process = fixture.CreateContext(fixture.EmployeeTenant);
            var result = await CreateProcessor(process, fixture).ProcessAsync(new DateOnly(2027, 3, 31));
            Assert.Equal(1, result.Processed);
            Assert.Equal(18m, await process.LeaveAccrualOccurrences.Select(x => x.CreditedQuantity).SingleAsync());
            Assert.Equal(18m, await process.LeaveBalanceTransactions.Where(x => x.TransactionType == LeaveBalanceTransactionType.Accrual).Select(x => x.Quantity).SingleAsync());
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_monthly_proration_disabled_credits_full_occurrence_after_eligibility()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL monthly no-proration test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                (await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId)).DateOfJoining = new DateOnly(2026, 10, 16);
                (await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId)).Status = EmployeeStatus.Terminated;
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.EntitlementQuantity = 1.5m;
                rule.AccrualFrequency = AccrualFrequency.Monthly;
                rule.AccrualTiming = AccrualTiming.EndOfPeriod;
                rule.ProratePartialPeriod = false;
                await setup.SaveChangesAsync();
            }

            await using var process = fixture.CreateContext(fixture.EmployeeTenant);
            Assert.Equal(0, (await CreateProcessor(process, fixture).ProcessAsync(new(2026, 10, 15))).Processed);
            Assert.Equal(1, (await CreateProcessor(process, fixture).ProcessAsync(new(2026, 10, 31))).Processed);
            Assert.Equal(1.5m, await process.LeaveAccrualOccurrences.Select(x => x.CreditedQuantity).SingleAsync());
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_accrual_restart_uses_persisted_occurrence_and_does_not_duplicate_state()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL accrual restart test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await ConfigureDailyTenantAsync(fixture);
            await using (var firstContext = fixture.CreateContext(fixture.EmployeeTenant))
                Assert.Equal(1, (await CreateProcessor(firstContext, fixture).ProcessAsync(new(2026, 10, 1))).Processed);

            await using (var verification = fixture.CreateContext(fixture.EmployeeTenant))
            {
                Assert.Equal(1, await verification.LeaveAccrualOccurrences.CountAsync(x => x.TenantId == fixture.TenantId && x.Status == "Processed"));
                Assert.Equal(1, await verification.LeaveBalanceTransactions.CountAsync(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.Accrual));
                Assert.Equal(1, await verification.LeaveEntitlementGrants.CountAsync(x => x.TenantId == fixture.TenantId && x.SourceType == LeaveBalanceSourceType.Policy));
                Assert.Equal(10.25m, await verification.EmployeeLeaveBalances.Where(x => x.Id == fixture.BalanceId).Select(x => x.GrantedQuantity).SingleAsync());
            }

            await using (var restartedContext = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var replay = await CreateProcessor(restartedContext, fixture).ProcessAsync(new(2026, 10, 1));
                Assert.Equal(0, replay.Processed);
                Assert.Equal(0m, replay.CreditedQuantity);
                Assert.Equal(1, await restartedContext.LeaveAccrualOccurrences.CountAsync(x => x.TenantId == fixture.TenantId && x.Status == "Processed"));
                Assert.Equal(1, await restartedContext.LeaveBalanceTransactions.CountAsync(x => x.TenantId == fixture.TenantId && x.TransactionType == LeaveBalanceTransactionType.Accrual));
                Assert.Equal(1, await restartedContext.LeaveEntitlementGrants.CountAsync(x => x.TenantId == fixture.TenantId && x.SourceType == LeaveBalanceSourceType.Policy));
                Assert.Equal(10.25m, await restartedContext.EmployeeLeaveBalances.Where(x => x.Id == fixture.BalanceId).Select(x => x.GrantedQuantity).SingleAsync());
            }
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_carry_forward_fixed_cap_posts_ten_and_lapses_eight_idempotently()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL fixed carry-forward test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var destinationId = Guid.NewGuid();
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                setup.LeavePeriods.Add(new LeavePeriod { Id = destinationId, TenantId = fixture.TenantId, Code = $"N{fixture.TenantId:N}"[..8], Name = "2027", StartDate = new(2027, 4, 1), EndDate = new(2028, 3, 31), IsActive = true });
                var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
                rule.CarryForwardEnabled = true;
                rule.MaximumCarryForwardQuantity = 10m;
                var balance = await setup.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
                balance.GrantedQuantity = 0m;
                await setup.SaveChangesAsync();
                var credit = await new LeaveBalanceTransactionPoster(setup, fixture.EmployeeTenant, TimeProvider.System).PostCreditAsync(new(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, fixture.LeavePeriodId, LeaveBalanceTransactionType.Opening, 18m, new(2026, 4, 1), null, null, LeaveBalanceSourceType.BalanceImport, "fixed-cap-source", LeaveBalanceActorType.System, null, null, "fixed-cap-source", null));
                Assert.True(credit.Succeeded, credit.Message);
            }
            await using (var closeContext = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var result = await CreateCloseProcessor(closeContext, fixture).ProcessAsync(fixture.LeavePeriodId, new(2027, 4, 1));
                Assert.Equal(1, result.Processed);
                Assert.Equal(10m, result.CarriedQuantity);
                Assert.Equal(8m, result.LapsedQuantity);
            }
            await using (var verification = fixture.CreateContext(fixture.EmployeeTenant))
            {
                Assert.Equal(10m, await verification.LeaveBalanceTransactions.Where(x => x.TransactionType == LeaveBalanceTransactionType.CarryForward).Select(x => x.Quantity).SingleAsync());
                Assert.Equal(8m, await verification.LeaveBalanceTransactions.Where(x => x.TransactionType == LeaveBalanceTransactionType.Expiry).Select(x => x.Quantity).SingleAsync());
                Assert.Equal(10m, await verification.LeaveEntitlementGrants.Where(x => x.SourceType == LeaveBalanceSourceType.CarryForward).Select(x => x.GrantedQuantity).SingleAsync());
                Assert.Equal(destinationId, await verification.LeaveEntitlementGrants.Where(x => x.SourceType == LeaveBalanceSourceType.CarryForward).Select(x => x.LeavePeriodId).SingleAsync());
            }
            await using (var replayContext = fixture.CreateContext(fixture.EmployeeTenant))
            {
                Assert.Equal(0, (await CreateCloseProcessor(replayContext, fixture).ProcessAsync(fixture.LeavePeriodId, new(2027, 4, 1))).Processed);
                Assert.Equal(1, await replayContext.LeavePeriodCloseOccurrences.CountAsync());
                Assert.Equal(1, await replayContext.LeaveBalanceTransactions.CountAsync(x => x.TransactionType == LeaveBalanceTransactionType.CarryForward));
                Assert.Equal(1, await replayContext.LeaveBalanceTransactions.CountAsync(x => x.TransactionType == LeaveBalanceTransactionType.Expiry));
            }
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_disabled_carry_forward_lapses_the_source_without_destination_credit()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL disabled carry-forward test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var destinationId = Guid.NewGuid();
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                setup.LeavePeriods.Add(new LeavePeriod { Id = destinationId, TenantId = fixture.TenantId, Code = $"N{fixture.TenantId:N}"[..8], Name = "2027", StartDate = new(2027, 4, 1), EndDate = new(2028, 3, 31), IsActive = true });
                var balance = await setup.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
                balance.GrantedQuantity = 0m;
                await setup.SaveChangesAsync();
                var credit = await new LeaveBalanceTransactionPoster(setup, fixture.EmployeeTenant, TimeProvider.System).PostCreditAsync(new(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, fixture.LeavePeriodId, LeaveBalanceTransactionType.Opening, 18m, new(2026, 4, 1), null, null, LeaveBalanceSourceType.BalanceImport, "disabled-source", LeaveBalanceActorType.System, null, null, "disabled-source", null));
                Assert.True(credit.Succeeded, credit.Message);
            }
            await using (var closeContext = fixture.CreateContext(fixture.EmployeeTenant))
                Assert.Equal(1, (await CreateCloseProcessor(closeContext, fixture).ProcessAsync(fixture.LeavePeriodId, new(2027, 4, 1))).Processed);
            await using (var verification = fixture.CreateContext(fixture.EmployeeTenant))
            {
                Assert.Equal(0, await verification.LeaveBalanceTransactions.CountAsync(x => x.TransactionType == LeaveBalanceTransactionType.CarryForward));
                Assert.Equal(18m, await verification.LeaveBalanceTransactions.Where(x => x.TransactionType == LeaveBalanceTransactionType.Expiry).Select(x => x.Quantity).SingleAsync());
                Assert.Equal(0, await verification.LeaveEntitlementGrants.CountAsync(x => x.SourceType == LeaveBalanceSourceType.CarryForward));
                Assert.Equal(0m, await verification.EmployeeLeaveBalances.Where(x => x.Id == fixture.BalanceId).Select(x => x.AvailableQuantity).SingleAsync());
            }
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_fully_consumed_carry_forward_never_expires_twice_after_restart()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL consumed carry-forward test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            Guid grantId;
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var balance = await setup.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
                balance.GrantedQuantity = 0m;
                await setup.SaveChangesAsync();
                var poster = new LeaveBalanceTransactionPoster(setup, fixture.EmployeeTenant, TimeProvider.System);
                Assert.True((await poster.PostCreditAsync(new(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, fixture.LeavePeriodId, LeaveBalanceTransactionType.CarryForward, 5m, new(2026, 1, 1), null, null, LeaveBalanceSourceType.CarryForward, "fully-consumed", LeaveBalanceActorType.System, null, null, "fully-consumed-credit", null, new(2026, 10, 2)))).Succeeded);
                grantId = await setup.LeaveEntitlementGrants.Where(x => x.SourceReference == "fully-consumed").Select(x => x.Id).SingleAsync();
                var submission = CreateSubmission(setup, fixture);
                var submitted = await submission.SubmitAsync(new(fixture.LeaveTypeId, new(2026, 10, 1), new(2026, 10, 5), "fully-consumed-request"));
                Assert.True(submitted.Succeeded, submitted.Message);
                var approval = new LeaveRequestApprovalService(setup, new EmployeeIdentityResolver(setup, fixture.ManagerTenant), new EmployeeManagerResolver(setup, fixture.ManagerTenant), new MySqlLeaveRequestSubmissionLock(setup), TimeProvider.System, balanceAccountingService: new LeaveBalanceAccountingService(setup, fixture.ManagerTenant, TimeProvider.System));
                Assert.True((await approval.ApproveAsync(submitted.Value!.RequestId)).Succeeded);
            }
            await using (var expiryContext = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var result = await new LeaveEntitlementExpiryProcessor(expiryContext, fixture.EmployeeTenant, new LeaveBalanceTransactionPoster(expiryContext, fixture.EmployeeTenant, TimeProvider.System)).ProcessAsync(new(2026, 10, 2));
                Assert.Equal(0, result.Processed);
                Assert.Equal(0, result.ExpiredQuantity);
                Assert.Equal(0, await expiryContext.LeaveBalanceTransactions.CountAsync(x => x.TransactionType == LeaveBalanceTransactionType.Expiry && x.SourceReference == $"LeaveEntitlementGrant:{grantId:D}"));
            }
            await using (var restartedExpiryContext = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var result = await new LeaveEntitlementExpiryProcessor(restartedExpiryContext, fixture.EmployeeTenant, new LeaveBalanceTransactionPoster(restartedExpiryContext, fixture.EmployeeTenant, TimeProvider.System)).ProcessAsync(new(2026, 10, 2));
                Assert.Equal(0, result.Processed);
                var grant = await restartedExpiryContext.LeaveEntitlementGrants.AsNoTracking().SingleAsync(x => x.Id == grantId);
                Assert.Equal(5m, grant.ConsumedQuantity);
                Assert.Equal(0m, grant.ExpiredQuantity);
                Assert.Equal(0m, grant.AvailableQuantity);
                Assert.Equal(0, await restartedExpiryContext.LeaveBalanceTransactions.CountAsync(x => x.TransactionType == LeaveBalanceTransactionType.Expiry && x.SourceReference == $"LeaveEntitlementGrant:{grantId:D}"));
            }
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_reservation_uses_earliest_expiry_sources_and_approval_preserves_allocation()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL source ordering test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var balance = await setup.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
                balance.GrantedQuantity = 0m;
                await setup.SaveChangesAsync();
                var poster = new LeaveBalanceTransactionPoster(setup, fixture.EmployeeTenant, TimeProvider.System);
                Assert.True((await poster.PostCreditAsync(new(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, fixture.LeavePeriodId, LeaveBalanceTransactionType.CarryForward, 5m, new(2026, 1, 1), null, null, LeaveBalanceSourceType.CarryForward, "earliest-cf", LeaveBalanceActorType.System, null, null, "earliest-cf", null, new(2026, 10, 2)))).Succeeded);
                Assert.True((await poster.PostCreditAsync(new(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, fixture.LeavePeriodId, LeaveBalanceTransactionType.Opening, 10m, new(2026, 1, 1), null, null, LeaveBalanceSourceType.BalanceImport, "later-opening", LeaveBalanceActorType.System, null, null, "later-opening", null))).Succeeded);
            }
            Guid requestId;
            await using (var requestContext = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var submission = CreateSubmission(requestContext, fixture);
                var submitted = await submission.SubmitAsync(new(fixture.LeaveTypeId, new(2026, 10, 1), new(2026, 10, 7), "earliest-order-request"));
                Assert.True(submitted.Succeeded, submitted.Message);
                requestId = submitted.Value!.RequestId;
                var allocations = await requestContext.LeaveBalanceReservationAllocations.Include(x => x.LeaveEntitlementGrant).Where(x => x.LeaveRequestId == requestId).ToListAsync();
                Assert.Equal(2, allocations.Count);
                Assert.Equal(5m, allocations.Single(x => x.LeaveEntitlementGrant!.SourceReference == "earliest-cf").ReservedQuantity);
                Assert.Equal(2m, allocations.Single(x => x.LeaveEntitlementGrant!.SourceReference == "later-opening").ReservedQuantity);
                var approval = new LeaveRequestApprovalService(requestContext, new EmployeeIdentityResolver(requestContext, fixture.ManagerTenant), new EmployeeManagerResolver(requestContext, fixture.ManagerTenant), new MySqlLeaveRequestSubmissionLock(requestContext), TimeProvider.System, balanceAccountingService: new LeaveBalanceAccountingService(requestContext, fixture.ManagerTenant, TimeProvider.System));
                Assert.True((await approval.ApproveAsync(requestId)).Succeeded);
            }
            await using var verification = fixture.CreateContext(fixture.EmployeeTenant);
            var grants = await verification.LeaveEntitlementGrants.Where(x => x.SourceReference == "earliest-cf" || x.SourceReference == "later-opening").ToListAsync();
            Assert.Equal(5m, grants.Single(x => x.SourceReference == "earliest-cf").ConsumedQuantity);
            Assert.Equal(2m, grants.Single(x => x.SourceReference == "later-opening").ConsumedQuantity);
            var expiryTransactions = await verification.LeaveBalanceTransactions.Where(x => x.TransactionType == LeaveBalanceTransactionType.Expiry).ToListAsync();
            Assert.DoesNotContain(expiryTransactions, x => x.SourceReference!.Contains(grants.Single(y => y.SourceReference == "earliest-cf").Id.ToString("D")));
        }
        finally { await fixture.CleanupAsync(); }
    }

    [Fact]
    public async Task MySql_adjacent_leave_periods_select_new_period_and_policy_at_boundary()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Leave Period boundary test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        var destinationId = Guid.NewGuid(); var version2Id = Guid.NewGuid(); var rule2Id = Guid.NewGuid();
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var source = await setup.LeavePeriods.SingleAsync(x => x.Id == fixture.LeavePeriodId);
                source.StartDate = new(2026, 4, 1); source.EndDate = new(2027, 3, 31); source.Name = "2026-27";
                (await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId)).Status = EmployeeStatus.Terminated;
                var v1 = await setup.LeavePolicyVersions.SingleAsync(x => x.Id == fixture.PolicyVersionId); v1.EffectiveFrom = source.StartDate; v1.EffectiveTo = source.EndDate;
                var rule1 = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId); rule1.EntitlementQuantity = 1m; rule1.AccrualFrequency = AccrualFrequency.Yearly; rule1.AccrualTiming = AccrualTiming.EndOfPeriod; rule1.ProratePartialPeriod = false;
                setup.LeavePeriods.Add(new LeavePeriod { Id = destinationId, TenantId = fixture.TenantId, Code = $"B{fixture.TenantId:N}"[..8], Name = "2027-28", StartDate = new(2027, 4, 1), EndDate = new(2028, 3, 31), IsActive = true });
                setup.LeavePolicyVersions.Add(new LeavePolicyVersion { Id = version2Id, TenantId = fixture.TenantId, LeavePolicyId = fixture.PolicyId, VersionNumber = 2, EffectiveFrom = new(2027, 4, 1), EffectiveTo = new(2028, 3, 31), Status = LeavePolicyVersionStatus.Published, Priority = 1 });
                setup.LeavePolicyRules.Add(new LeavePolicyRule { Id = rule2Id, TenantId = fixture.TenantId, LeavePolicyVersionId = version2Id, LeaveTypeId = fixture.LeaveTypeId, IsActive = true });
                setup.LeavePolicyEntitlementRules.Add(new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, LeavePolicyRuleId = rule2Id, EntitlementMode = EntitlementMode.Allocated, EntitlementSource = EntitlementSource.PolicyAccrual, EntitlementQuantity = 2m, AccrualFrequency = AccrualFrequency.Yearly, AccrualTiming = AccrualTiming.EndOfPeriod, ProratePartialPeriod = false });
                await setup.SaveChangesAsync();
            }
            await using (var marchContext = fixture.CreateContext(fixture.EmployeeTenant))
            { Assert.Equal(1, (await CreateProcessor(marchContext, fixture).ProcessAsync(new(2027, 3, 31))).Processed); }
            await using (var aprilContext = fixture.CreateContext(fixture.EmployeeTenant))
            { Assert.Equal(1, (await CreateProcessor(aprilContext, fixture).ProcessAsync(new(2028, 3, 31))).Processed); }
            await using (var verification = fixture.CreateContext(fixture.EmployeeTenant))
            {
                var occurrences = await verification.LeaveAccrualOccurrences.OrderBy(x => x.OccurrenceDate).ToListAsync();
                Assert.Equal(2, occurrences.Count);
                Assert.Equal(fixture.LeavePeriodId, occurrences[0].LeavePeriodId); Assert.Equal(fixture.PolicyVersionId, occurrences[0].LeavePolicyVersionId); Assert.Equal(1m, occurrences[0].CreditedQuantity);
                Assert.Equal(destinationId, occurrences[1].LeavePeriodId); Assert.Equal(version2Id, occurrences[1].LeavePolicyVersionId); Assert.Equal(2m, occurrences[1].CreditedQuantity);
                Assert.Equal(2, await verification.LeaveBalanceTransactions.CountAsync(x => x.TransactionType == LeaveBalanceTransactionType.Accrual));
            }
            await using (var replayContext = fixture.CreateContext(fixture.EmployeeTenant))
            { Assert.Equal(0, (await CreateProcessor(replayContext, fixture).ProcessAsync(new(2028, 3, 31))).Processed); }
        }
        finally { await fixture.CleanupAsync(); }
    }

    private static LeaveAccrualProcessor CreateProcessor(HRMS.Infrastructure.Persistence.HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture fixture)
    {
        var employment = new EffectiveEmploymentResolver(db, fixture.EmployeeTenant);
        return new LeaveAccrualProcessor(
            db,
            fixture.EmployeeTenant,
            new LeavePolicyResolver(db, employment, fixture.EmployeeTenant),
            new LeavePeriodResolver(db, fixture.EmployeeTenant),
            new LeaveBalanceTransactionPoster(db, fixture.EmployeeTenant, TimeProvider.System),
            TimeProvider.System);
    }

    private static LeavePeriodCloseProcessor CreateCloseProcessor(HRMS.Infrastructure.Persistence.HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture fixture)
    {
        var employment = new EffectiveEmploymentResolver(db, fixture.EmployeeTenant);
        return new LeavePeriodCloseProcessor(db, fixture.EmployeeTenant, new LeavePolicyResolver(db, employment, fixture.EmployeeTenant), new LeaveBalanceTransactionPoster(db, fixture.EmployeeTenant, TimeProvider.System), TimeProvider.System);
    }

    private static LeaveRequestSubmissionService CreateSubmission(HRMS.Infrastructure.Persistence.HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture fixture)
    {
        var employment = new EffectiveEmploymentResolver(db, fixture.EmployeeTenant);
        var validation = new LeaveRequestValidationService(db, new EmployeeIdentityResolver(db, fixture.EmployeeTenant), employment, new LeavePeriodResolver(db, fixture.EmployeeTenant), new LeavePolicyResolver(db, employment, fixture.EmployeeTenant));
        return new LeaveRequestSubmissionService(db, new EmployeeIdentityResolver(db, fixture.EmployeeTenant), validation, new MySqlLeaveRequestSubmissionLock(db), TimeProvider.System, balanceAccountingService: new LeaveBalanceAccountingService(db, fixture.EmployeeTenant, TimeProvider.System));
    }

    private static async Task ConfigureDailyTenantAsync(MySqlLeaveLifecycleIntegrationTests.Fixture fixture)
    {
        await using var setup = fixture.CreateContext(fixture.EmployeeTenant);
        (await setup.Employees.SingleAsync(x => x.Id == fixture.EmployeeId)).DateOfJoining = new DateOnly(2026, 10, 1);
        (await setup.Employees.SingleAsync(x => x.Id == fixture.ManagerId)).Status = EmployeeStatus.Terminated;
        var rule = await setup.LeavePolicyEntitlementRules.SingleAsync(x => x.LeavePolicyRuleId == fixture.PolicyRuleId);
        rule.EntitlementQuantity = 0.25m;
        rule.AccrualFrequency = AccrualFrequency.Daily;
        await setup.SaveChangesAsync();
    }
}
