using HRMS.Application.Services;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlAttendanceMonthlyIntegrationTests
{
    [Fact]
    public async Task MySql_monthly_period_summary_and_derived_exceptions_persist_and_reprocess()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("Phase 5B MySQL test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var db = fixture.CreateContext(fixture.EmployeeTenant);
            var processor = new AttendanceMonthlyProcessor(db, fixture.EmployeeTenant);

            var created = await processor.CreatePeriodAsync(new(2026, 10));
            Assert.True(created.Succeeded, created.Message);

            var first = await processor.ProcessAsync(created.Value!.Id);
            var firstSummaryCount = await db.EmployeeAttendanceMonthlySummaries.CountAsync(x => x.AttendancePeriodId == created.Value.Id);
            var second = await processor.ProcessAsync(created.Value.Id);

            Assert.True(first.Succeeded, first.Message);
            Assert.True(second.Succeeded, second.Message);
            Assert.Equal(AttendancePeriodStatus.ReadyToClose, second.Value!.Status);
            Assert.True(firstSummaryCount > 0);
            Assert.Equal(firstSummaryCount, await db.EmployeeAttendanceMonthlySummaries.AsNoTracking().CountAsync(x => x.AttendancePeriodId == created.Value.Id));
            Assert.NotEmpty((await processor.GetExceptionsAsync(created.Value.Id, new())).Value!.Items);
            Assert.Equal(2, await db.AttendancePeriodEvents.CountAsync(x => x.AttendancePeriodId == created.Value.Id && x.EventType == AttendancePeriodEventType.ProcessingCompleted));
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            await cleanup.EmployeeAttendanceMonthlySummaries.ExecuteDeleteAsync();
            await cleanup.AttendancePeriodEvents.ExecuteDeleteAsync();
            await cleanup.AttendancePeriods.ExecuteDeleteAsync();
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_monthly_processing_is_safe_when_two_contexts_race()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("Phase 5B MySQL concurrency test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var setup = fixture.CreateContext(fixture.EmployeeTenant);
            var periodResult = await new AttendanceMonthlyProcessor(setup, fixture.EmployeeTenant).CreatePeriodAsync(new(2026, 11));
            Assert.True(periodResult.Succeeded, periodResult.Message);

            await using var dbA = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            await using var dbB = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            var calls = await Task.WhenAll(
                new AttendanceMonthlyProcessor(dbA, new TestTenantContext(fixture.TenantId)).ProcessAsync(periodResult.Value!.Id),
                new AttendanceMonthlyProcessor(dbB, new TestTenantContext(fixture.TenantId)).ProcessAsync(periodResult.Value.Id));

            Assert.Contains(calls, x => x.Succeeded);
            Assert.All(calls, x => Assert.True(x.Succeeded || x.Status == HRMS.Application.Common.ResultStatus.Conflict || x.Status == HRMS.Application.Common.ResultStatus.ServiceUnavailable));
            await using var verify = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            Assert.Equal(AttendancePeriodStatus.ReadyToClose, await verify.AttendancePeriods.Where(x => x.Id == periodResult.Value.Id).Select(x => x.Status).SingleAsync());
            Assert.True(await verify.EmployeeAttendanceMonthlySummaries.CountAsync(x => x.AttendancePeriodId == periodResult.Value.Id) > 0);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            await cleanup.EmployeeAttendanceMonthlySummaries.ExecuteDeleteAsync();
            await cleanup.AttendancePeriodEvents.ExecuteDeleteAsync();
            await cleanup.AttendancePeriods.ExecuteDeleteAsync();
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_ready_period_can_close_reopen_and_reclose()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("Phase 5C MySQL test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var db = fixture.CreateContext(fixture.EmployeeTenant);
            var processor = new AttendanceMonthlyProcessor(db, fixture.EmployeeTenant);
            var period = (await processor.CreatePeriodAsync(new(2019, 12))).Value!;
            Assert.True((await processor.ProcessAsync(period.Id)).Succeeded);
            Assert.True((await processor.GetClosePreviewAsync(period.Id)).Value!.CanClose);
            Assert.True((await processor.CloseAsync(period.Id)).Succeeded);
            var reopened = await processor.ReopenAsync(period.Id, new("MySQL close/reopen verification."));
            Assert.True(reopened.Succeeded, reopened.Message);
            Assert.Equal(2, reopened.Value!.DataVersion);
            Assert.True((await processor.ProcessAsync(period.Id)).Succeeded);
            Assert.True((await processor.CloseAsync(period.Id)).Succeeded);
            Assert.Contains(await db.AttendancePeriodEvents.AsNoTracking().Where(x => x.AttendancePeriodId == period.Id).Select(x => x.EventType).ToListAsync(), x => x == AttendancePeriodEventType.Reopened);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            await cleanup.AttendancePeriodEvents.ExecuteDeleteAsync();
            await cleanup.AttendancePeriods.ExecuteDeleteAsync();
            await fixture.CleanupAsync();
        }
    }
}
