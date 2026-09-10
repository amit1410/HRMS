using HRMS.Application.Services;
using HRMS.Application.Abstractions;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using HRMS.Tests.TestSupport;
using Xunit.Sdk;

namespace HRMS.Tests;

/// <summary>Real-MySQL race coverage for the durable Leave reminder claim.</summary>
public sealed class MySqlLeaveReminderConcurrencyTests
{
    [Fact]
    public async Task Two_independent_processors_only_one_claims_and_sends_one_occurrence()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("MySQL Leave reminder concurrency test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixtureType = typeof(MySqlLeaveLifecycleIntegrationTests)
            .GetNestedType("Fixture", System.Reflection.BindingFlags.NonPublic)!;
        var fixture = Activator.CreateInstance(
            fixtureType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [connection],
            culture: null)!;

        try
        {
            await InvokeAsync(fixture, "SeedAsync");
            var employeeTenant = Get<object>(fixture, "EmployeeTenant");
            var createContext = fixtureType.GetMethod("CreateContext")!;
            await using var setup = (HrmsDbContext)createContext.Invoke(fixture, [employeeTenant])!;

            var validation = new LeaveRequestValidationService(
                setup,
                new EmployeeIdentityResolver(setup, (HRMS.Application.Abstractions.ITenantContext)employeeTenant),
                new EffectiveEmploymentResolver(setup, (HRMS.Application.Abstractions.ITenantContext)employeeTenant),
                new LeavePeriodResolver(setup, (HRMS.Application.Abstractions.ITenantContext)employeeTenant),
                new LeavePolicyResolver(setup, new EffectiveEmploymentResolver(setup, (HRMS.Application.Abstractions.ITenantContext)employeeTenant), (HRMS.Application.Abstractions.ITenantContext)employeeTenant));
            var notification = fixtureType.GetMethod("CreateNotificationService")!.Invoke(fixture, [setup])!;
            var submission = new LeaveRequestSubmissionService(
                setup,
                new EmployeeIdentityResolver(setup, (HRMS.Application.Abstractions.ITenantContext)employeeTenant),
                validation,
                new MySqlLeaveRequestSubmissionLock(setup),
                TimeProvider.System,
                balanceAccountingService: new LeaveBalanceAccountingService(setup, (HRMS.Application.Abstractions.ITenantContext)employeeTenant, TimeProvider.System),
                notificationService: (HRMS.Application.Abstractions.ILeaveNotificationService)notification);
            var submitted = await submission.SubmitAsync(new(
                Get<Guid>(fixture, "LeaveTypeId"),
                Get<DateOnly>(fixture, "RequestDate"),
                Get<DateOnly>(fixture, "RequestDate"),
                "mysql-reminder-concurrency"));
            Assert.True(submitted.Succeeded);

            var request = await setup.LeaveRequests.SingleAsync(x => x.Id == submitted.Value!.RequestId);
            request.SubmittedAtUtc = DateTime.UtcNow.AddHours(-48);
            await setup.SaveChangesAsync();
            Get<System.Collections.IList>(fixture, "Email.Messages").Clear();

            await using var contextA = (HrmsDbContext)createContext.Invoke(fixture, [employeeTenant])!;
            await using var contextB = (HrmsDbContext)createContext.Invoke(fixture, [employeeTenant])!;
            var clock = new FixedClock(DateTimeOffset.UtcNow);
            var processorA = CreateProcessor(fixture, contextA, employeeTenant, clock);
            var processorB = CreateProcessor(fixture, contextB, employeeTenant, clock);

            var results = await Task.WhenAll(processorA.ProcessAsync(), processorB.ProcessAsync());

            Assert.Equal(1, results.Count(x => x.Sent == 1));
            Assert.Equal(1, results.Count(x => x.Sent == 0));
            Assert.Single(Get<System.Collections.IList>(fixture, "Email.Messages"));
            var rows = await contextA.LeaveReminderDeliveries.AsNoTracking().Where(x => x.LeaveRequestId == submitted.Value.RequestId).ToListAsync();
            var row = Assert.Single(rows);
            Assert.Equal("Sent", row.Status);
            Assert.Null(row.ClaimToken);
            Assert.Equal(1, row.AttemptCount);

            var complete = typeof(LeaveApprovalReminderProcessor)
                .GetMethod("CompleteAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var occurrenceKey = row.OccurrenceKey;
            var stale = (Task)complete.Invoke(
                processorB,
                [row.TenantId, occurrenceKey, Guid.NewGuid(), true, clock.GetUtcNow().UtcDateTime, CancellationToken.None])!;
            await stale;
            var afterStale = await contextA.LeaveReminderDeliveries.AsNoTracking().SingleAsync(x => x.Id == row.Id);
            Assert.Equal("Sent", afterStale.Status);
            Assert.Equal(1, await contextA.LeaveReminderDeliveries.AsNoTracking().CountAsync(x => x.LeaveRequestId == submitted.Value.RequestId && x.Status == "Sent"));
        }
        finally
        {
            await InvokeAsync(fixture, "CleanupAsync");
            if (fixture is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
    }

    [Fact]
    public async Task MySql_failed_reminder_persists_and_recreated_processor_suppresses_or_retries_by_window()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("MySQL Leave reminder retry test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixtureType = typeof(MySqlLeaveLifecycleIntegrationTests)
            .GetNestedType("Fixture", System.Reflection.BindingFlags.NonPublic)!;
        var fixture = Activator.CreateInstance(fixtureType,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            null, [connection], null)!;
        try
        {
            await InvokeAsync(fixture, "SeedAsync");
            var tenant = Get<object>(fixture, "EmployeeTenant");
            var create = fixtureType.GetMethod("CreateContext")!;
            await using var setup = (HrmsDbContext)create.Invoke(fixture, [tenant])!;
            var requestId = await SubmitAsync(fixture, setup, tenant);
            Get<System.Collections.IList>(fixture, "Email.Messages").Clear();
            var request = await setup.LeaveRequests.SingleAsync(x => x.Id == requestId);
            request.SubmittedAtUtc = DateTime.UtcNow.AddHours(-48);
            await setup.SaveChangesAsync();

            var email = Get<object>(fixture, "Email");
            email.GetType().GetProperty("ThrowOnSend")!.SetValue(email, true);
            var clock = new FixedClock(DateTimeOffset.UtcNow);
            await using (var failedContext = (HrmsDbContext)create.Invoke(fixture, [tenant])!)
            {
                var failed = await CreateProcessor(fixture, failedContext, tenant, clock).ProcessAsync();
                Assert.Equal(1, failed.Failed);
                var failedRow = await failedContext.LeaveReminderDeliveries.AsNoTracking().SingleAsync(x => x.LeaveRequestId == requestId);
                Assert.Equal("Failed", failedRow.Status);
                Assert.Equal(1, failedRow.AttemptCount);
                Assert.Equal(LeaveRequestStatus.PendingApproval, await failedContext.LeaveRequests.Where(x => x.Id == requestId).Select(x => x.Status).SingleAsync());
            }

            email.GetType().GetProperty("ThrowOnSend")!.SetValue(email, false);
            await using (var recreatedContext = (HrmsDbContext)create.Invoke(fixture, [tenant])!)
            {
                var recreated = await CreateProcessor(fixture, recreatedContext, tenant, clock).ProcessAsync();
                Assert.Equal(0, recreated.Sent);
                Assert.Empty(Get<System.Collections.IList>(fixture, "Email.Messages"));
            }

            clock.Now = clock.Now.AddHours(24);
            await using (var retryContext = (HrmsDbContext)create.Invoke(fixture, [tenant])!)
            {
                var retry = await CreateProcessor(fixture, retryContext, tenant, clock).ProcessAsync();
                Assert.Equal(1, retry.Sent);
                Assert.Equal(1, await retryContext.LeaveReminderDeliveries.AsNoTracking().CountAsync(x => x.LeaveRequestId == requestId && x.Status == "Sent"));
            }
        }
        finally
        {
            await InvokeAsync(fixture, "CleanupAsync");
        }
    }

    private static ILeaveApprovalReminderProcessor CreateProcessor(
        object fixture,
        HrmsDbContext context,
        object tenant,
        TimeProvider clock)
    {
        var notification = typeof(MySqlLeaveLifecycleIntegrationTests)
            .GetNestedType("Fixture", System.Reflection.BindingFlags.NonPublic)!
            .GetMethod("CreateNotificationService")!
            .Invoke(fixture, [context])!;
        return new LeaveApprovalReminderProcessor(
            context,
            new EmployeeManagerResolver(context, (HRMS.Application.Abstractions.ITenantContext)tenant, clock),
            (HRMS.Application.Abstractions.ILeaveNotificationService)notification,
            new HRMS.Application.Abstractions.LeaveReminderOptions { InitialDelayHours = 24, RepeatIntervalHours = 24, ClaimLeaseMinutes = 5 },
            clock,
            NullLogger<LeaveApprovalReminderProcessor>.Instance);
    }

    private static async Task<Guid> SubmitAsync(object fixture, HrmsDbContext db, object tenant)
    {
        var validation = new LeaveRequestValidationService(
            db,
            new EmployeeIdentityResolver(db, (HRMS.Application.Abstractions.ITenantContext)tenant),
            new EffectiveEmploymentResolver(db, (HRMS.Application.Abstractions.ITenantContext)tenant),
            new LeavePeriodResolver(db, (HRMS.Application.Abstractions.ITenantContext)tenant),
            new LeavePolicyResolver(db, new EffectiveEmploymentResolver(db, (HRMS.Application.Abstractions.ITenantContext)tenant), (HRMS.Application.Abstractions.ITenantContext)tenant));
        var fixtureType = fixture.GetType();
        var notification = fixtureType.GetMethod("CreateNotificationService")!.Invoke(fixture, [db])!;
        var result = await new LeaveRequestSubmissionService(
            db,
            new EmployeeIdentityResolver(db, (HRMS.Application.Abstractions.ITenantContext)tenant),
            validation,
            new MySqlLeaveRequestSubmissionLock(db),
            TimeProvider.System,
            balanceAccountingService: new LeaveBalanceAccountingService(db, (HRMS.Application.Abstractions.ITenantContext)tenant, TimeProvider.System),
            notificationService: (HRMS.Application.Abstractions.ILeaveNotificationService)notification)
            .SubmitAsync(new(Get<Guid>(fixture, "LeaveTypeId"), Get<DateOnly>(fixture, "RequestDate"), Get<DateOnly>(fixture, "RequestDate"), "mysql-reminder-retry"));
        Assert.True(result.Succeeded);
        return result.Value!.RequestId;
    }

    private static async Task InvokeAsync(object target, string method)
    {
        var task = (Task)target.GetType().GetMethod(method)!.Invoke(target, null)!;
        await task;
    }

    private static T Get<T>(object target, string property)
    {
        var current = target;
        foreach (var part in property.Split('.'))
            current = current.GetType().GetProperty(part)!.GetValue(current)!;
        return (T)current;
    }
}
