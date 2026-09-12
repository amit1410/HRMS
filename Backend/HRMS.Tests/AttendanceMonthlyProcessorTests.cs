using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HRMS.Tests;

public sealed class AttendanceMonthlyProcessorTests
{
    [Fact]
    public async Task Period_creation_sets_calendar_boundaries_open_state_and_event()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        var scope = new TestTenantContext(tenantId);

        await using var context = database.CreateContext(scope);
        var processor = new AttendanceMonthlyProcessor(context, scope);
        var result = await processor.CreatePeriodAsync(new(2026, 2));

        Assert.True(result.Succeeded);
        Assert.Equal(new DateOnly(2026, 2, 1), result.Value!.StartDate);
        Assert.Equal(new DateOnly(2026, 2, 28), result.Value.EndDate);
        Assert.Equal(AttendancePeriodStatus.Open, result.Value.Status);
        Assert.Equal(1, result.Value.DataVersion);
        Assert.Single(await context.AttendancePeriodEvents.ToListAsync());
    }

    [Fact]
    public async Task Monthly_processing_aggregates_daily_statuses_variances_minutes_and_is_idempotent()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        var scope = new TestTenantContext(tenantId);
        await using (var seed = database.CreateContext(scope))
        {
            seed.Employees.Add(Employee(tenantId, employeeId, new(2026, 1, 1), null));
            seed.EmployeeEmploymentHistory.Add(History(tenantId, employeeId, new(2026, 1, 1), null));
            seed.EmployeeAttendanceDays.AddRange(
                Day(tenantId, employeeId, new(2026, 2, 1), EmployeeAttendanceDayStatus.Present, 480, 450, late: true),
                Day(tenantId, employeeId, new(2026, 2, 2), EmployeeAttendanceDayStatus.Absent, 480, null),
                Day(tenantId, employeeId, new(2026, 2, 3), EmployeeAttendanceDayStatus.OnLeave, null, null),
                Day(tenantId, employeeId, new(2026, 2, 4), EmployeeAttendanceDayStatus.OnDuty, 480, 480),
                Day(tenantId, employeeId, new(2026, 2, 5), EmployeeAttendanceDayStatus.Holiday, null, null),
                Day(tenantId, employeeId, new(2026, 2, 6), EmployeeAttendanceDayStatus.WeeklyOff, null, null),
                Day(tenantId, employeeId, new(2026, 2, 7), EmployeeAttendanceDayStatus.Incomplete, 480, 120, missingOut: true),
                Day(tenantId, employeeId, new(2026, 2, 8), EmployeeAttendanceDayStatus.NotProcessed, null, null));
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext(scope);
        var processor = new AttendanceMonthlyProcessor(context, scope);
        var period = (await processor.CreatePeriodAsync(new(2026, 2))).Value!;
        var first = await processor.ProcessAsync(period.Id);
        var firstSummary = await context.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync();
        Assert.Equal(1, firstSummary.IncompleteDays);
        var second = await processor.ProcessAsync(period.Id);
        var summary = firstSummary;

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(AttendancePeriodStatus.ReadyToClose, second.Value!.Status);
        Assert.Equal(1, await context.EmployeeAttendanceMonthlySummaries.AsNoTracking().CountAsync());
        Assert.Equal(1, summary.PresentDays); Assert.Equal(1, summary.AbsentDays); Assert.Equal(1, summary.OnLeaveDays);
        Assert.Equal(1, summary.OnDutyDays); Assert.Equal(1, summary.HolidayDays); Assert.Equal(1, summary.WeeklyOffDays);
        Assert.Equal(1, summary.IncompleteDays); Assert.Equal(21, summary.NotProcessedDays);
        Assert.Equal(1, summary.LateInCount); Assert.Equal(1, summary.MissingOutCount);
        Assert.Equal(1920, summary.ExpectedWorkMinutes); Assert.Equal(1050, summary.ActualWorkMinutes);
        Assert.Equal(2, await context.AttendancePeriodEvents.CountAsync(x => x.EventType == AttendancePeriodEventType.ProcessingCompleted));
    }

    [Fact]
    public async Task Population_excludes_dates_outside_effective_employment_and_derives_pending_exceptions()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var joinerId = Guid.NewGuid();
        var userId = Guid.NewGuid(); var regId = Guid.NewGuid(); var odId = Guid.NewGuid(); var outsideRegId = Guid.NewGuid(); var outsideOdId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        var scope = new TestTenantContext(tenantId, userId);
        await using (var seed = database.CreateContext(scope))
        {
            seed.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "processor@test.local", FirstName = "Test", LastName = "Processor" });
            seed.Employees.AddRange(Employee(tenantId, employeeId, new(2026, 2, 10), new(2026, 2, 20)), Employee(tenantId, joinerId, new(2026, 3, 1), null));
            seed.EmployeeEmploymentHistory.Add(History(tenantId, employeeId, new(2026, 2, 10), new(2026, 2, 20)));
            seed.AttendanceRegularizationRequests.Add(new() { Id = regId, TenantId = tenantId, EmployeeId = employeeId, BusinessDate = new(2026, 2, 15), RequestType = AttendanceRegularizationType.MissingBothPunches, Reason = "test", SubmittedByUserId = userId, SubmittedAtUtc = DateTime.UtcNow });
            seed.AttendanceOnDutyRequests.Add(new() { Id = odId, TenantId = tenantId, EmployeeId = employeeId, StartDate = new(2026, 2, 15), EndDate = new(2026, 3, 2), Reason = "test", SubmittedByUserId = userId, SubmittedAtUtc = DateTime.UtcNow });
            seed.AttendanceRegularizationRequests.Add(new() { Id = outsideRegId, TenantId = tenantId, EmployeeId = employeeId, BusinessDate = new(2026, 3, 1), RequestType = AttendanceRegularizationType.MissingBothPunches, Reason = "outside", SubmittedByUserId = userId, SubmittedAtUtc = DateTime.UtcNow });
            seed.AttendanceOnDutyRequests.Add(new() { Id = outsideOdId, TenantId = tenantId, EmployeeId = employeeId, StartDate = new(2026, 1, 1), EndDate = new(2026, 1, 2), Reason = "outside", SubmittedByUserId = userId, SubmittedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext(scope);
        var processor = new AttendanceMonthlyProcessor(context, scope);
        var period = (await processor.CreatePeriodAsync(new(2026, 2))).Value!;
        var processed = await processor.ProcessAsync(period.Id);
        var summaries = await context.EmployeeAttendanceMonthlySummaries.ToListAsync();
        var exceptions = (await processor.GetExceptionsAsync(period.Id, new())).Value!;

        Assert.True(processed.Succeeded); Assert.Single(summaries); Assert.Equal(11, summaries[0].EmploymentDays);
        Assert.Contains(exceptions.Items, x => x.ExceptionType == AttendanceExceptionType.PendingRegularization && x.IsBlocking);
        Assert.Contains(exceptions.Items, x => x.ExceptionType == AttendanceExceptionType.PendingOnDuty && x.IsBlocking);
        Assert.DoesNotContain(exceptions.Items, x => x.SourceId == outsideRegId || x.SourceId == outsideOdId);
        Assert.DoesNotContain(summaries, x => x.EmployeeId == joinerId);
    }

    [Fact]
    public async Task Period_and_exception_reads_are_tenant_scoped()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantA = Guid.NewGuid(); var tenantB = Guid.NewGuid();
        await SeedTenant(database, tenantA); await SeedTenant(database, tenantB);
        var scopeA = new TestTenantContext(tenantA);
        var scopeB = new TestTenantContext(tenantB);
        await using var contextA = database.CreateContext(scopeA);
        await using var contextB = database.CreateContext(scopeB);
        var periodA = (await new AttendanceMonthlyProcessor(contextA, scopeA).CreatePeriodAsync(new(2026, 2))).Value!;
        var result = await new AttendanceMonthlyProcessor(contextB, scopeB).GetPeriodAsync(periodA.Id);
        var exceptions = await new AttendanceMonthlyProcessor(contextB, scopeB).GetExceptionsAsync(periodA.Id, new());
        Assert.Equal(HRMS.Application.Common.ResultStatus.NotFound, result.Status);
        Assert.Equal(HRMS.Application.Common.ResultStatus.NotFound, exceptions.Status);
    }

    [Fact]
    public async Task Population_uses_effective_employment_boundaries_without_duplicate_or_nonemployment_rows()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid();
        var full = Guid.NewGuid(); var joiner = Guid.NewGuid(); var leaver = Guid.NewGuid();
        var future = Guid.NewGuid(); var ended = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        var scope = new TestTenantContext(tenantId);
        await using (var seed = database.CreateContext(scope))
        {
            seed.Employees.AddRange(
                Employee(tenantId, full, new(2026, 1, 1), null),
                Employee(tenantId, joiner, new(2026, 9, 11), null),
                Employee(tenantId, leaver, new(2026, 1, 1), new(2026, 9, 20)),
                Employee(tenantId, future, new(2026, 10, 1), null),
                Employee(tenantId, ended, new(2026, 1, 1), new(2026, 8, 31)));
            seed.EmployeeEmploymentHistory.AddRange(
                History(tenantId, full, new(2026, 1, 1), new(2026, 9, 15)),
                History(tenantId, full, new(2026, 9, 16), null),
                History(tenantId, joiner, new(2026, 9, 11), null),
                History(tenantId, leaver, new(2026, 1, 1), new(2026, 9, 20)),
                History(tenantId, future, new(2026, 10, 1), null),
                History(tenantId, ended, new(2026, 1, 1), new(2026, 8, 31)));
            await seed.SaveChangesAsync();
        }

        await using var context = database.CreateContext(scope);
        var processor = new AttendanceMonthlyProcessor(context, scope);
        var period = (await processor.CreatePeriodAsync(new(2026, 9))).Value!;
        Assert.True((await processor.ProcessAsync(period.Id)).Succeeded);
        var summaries = await context.EmployeeAttendanceMonthlySummaries.AsNoTracking().ToListAsync();

        Assert.Equal(3, summaries.Count);
        Assert.Equal(30, summaries.Single(x => x.EmployeeId == full).EmploymentDays);
        Assert.Equal(20, summaries.Single(x => x.EmployeeId == joiner).EmploymentDays);
        Assert.Equal(20, summaries.Single(x => x.EmployeeId == leaver).EmploymentDays);
        Assert.DoesNotContain(summaries, x => x.EmployeeId == future || x.EmployeeId == ended);

        var exceptions = (await processor.GetExceptionsAsync(period.Id, new())).Value!;
        Assert.DoesNotContain(exceptions.Items, x => x.EmployeeId == future || x.EmployeeId == ended);
    }

    [Fact]
    public async Task Processing_refreshes_source_version_and_counts_exception_items()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        var scope = new TestTenantContext(tenantId);
        await using (var seed = database.CreateContext(scope))
        {
            seed.Employees.Add(Employee(tenantId, employeeId, new(2026, 1, 1), null));
            seed.EmployeeEmploymentHistory.Add(History(tenantId, employeeId, new(2026, 1, 1), null));
            seed.EmployeeAttendanceDays.Add(Day(tenantId, employeeId, new(2026, 9, 1), EmployeeAttendanceDayStatus.Present, 480, 450, late: true, early: true, grace: true, missingIn: true, missingOut: true));
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext(scope);
        var processor = new AttendanceMonthlyProcessor(context, scope);
        var period = (await processor.CreatePeriodAsync(new(2026, 9))).Value!;
        Assert.True((await processor.ProcessAsync(period.Id)).Succeeded);
        var first = await context.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync();
        // The two punch exceptions plus one NotProcessed item for each remaining employed date.
        Assert.Equal(31, first.ExceptionCount);
        Assert.Equal(1, first.LateInCount); Assert.Equal(1, first.EarlyOutCount); Assert.Equal(1, first.GraceAppliedCount);
        Assert.Equal(1, first.MissingInCount); Assert.Equal(1, first.MissingOutCount);

        var periodEntity = await context.AttendancePeriods.SingleAsync(x => x.Id == period.Id);
        periodEntity.DataVersion++;
        await context.SaveChangesAsync();
        Assert.True((await processor.ProcessAsync(period.Id)).Succeeded);
        var refreshed = await context.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync();
        Assert.Equal(periodEntity.DataVersion, refreshed.SourceDataVersion);
        Assert.Equal(1, await context.EmployeeAttendanceMonthlySummaries.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Concurrent_processing_from_independent_contexts_leaves_one_consistent_summary_set()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Employees.Add(Employee(tenantId, employeeId, new(2026, 1, 1), null));
            seed.EmployeeEmploymentHistory.Add(History(tenantId, employeeId, new(2026, 1, 1), null));
            await seed.SaveChangesAsync();
        }

        var setupScope = new TestTenantContext(tenantId);
        await using var setup = database.CreateContext(setupScope);
        var period = (await new AttendanceMonthlyProcessor(setup, setupScope).CreatePeriodAsync(new(2026, 9))).Value!;
        await using var dbA = database.CreateContext(new TestTenantContext(tenantId));
        await using var dbB = database.CreateContext(new TestTenantContext(tenantId));
        var calls = await Task.WhenAll(
            new AttendanceMonthlyProcessor(dbA, new TestTenantContext(tenantId)).ProcessAsync(period.Id),
            new AttendanceMonthlyProcessor(dbB, new TestTenantContext(tenantId)).ProcessAsync(period.Id));

        Assert.Contains(calls, x => x.Succeeded);
        Assert.All(calls, x => Assert.True(x.Succeeded || x.Status == HRMS.Application.Common.ResultStatus.Conflict || x.Status == HRMS.Application.Common.ResultStatus.ServiceUnavailable));
        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        var persisted = await verify.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id);
        Assert.Equal(AttendancePeriodStatus.ReadyToClose, persisted.Status);
        Assert.Equal(1, await verify.EmployeeAttendanceMonthlySummaries.CountAsync(x => x.AttendancePeriodId == period.Id));
    }

    [Fact]
    public async Task Processing_failure_rolls_back_summary_replacement_and_retry_succeeds()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid();
        await SeedTenant(database, tenantId);
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Employees.Add(Employee(tenantId, employeeId, new(2026, 1, 1), null));
            seed.EmployeeEmploymentHistory.Add(History(tenantId, employeeId, new(2026, 1, 1), null));
            await seed.SaveChangesAsync();
        }

        var scope = new TestTenantContext(tenantId);
        await using var initialDb = database.CreateContext(scope);
        var initialProcessor = new AttendanceMonthlyProcessor(initialDb, scope);
        var period = (await initialProcessor.CreatePeriodAsync(new(2026, 9))).Value!;
        Assert.True((await initialProcessor.ProcessAsync(period.Id)).Succeeded);
        var before = await initialDb.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync();

        await using (var failingDb = database.CreateContext(new TestTenantContext(tenantId), new FailSummaryInsertInterceptor()))
        {
            var failed = await new AttendanceMonthlyProcessor(failingDb, new TestTenantContext(tenantId)).ProcessAsync(period.Id);
            Assert.False(failed.Succeeded);
        }

        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        var afterFailure = await verify.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id);
        Assert.Equal(AttendancePeriodStatus.ReadyToClose, afterFailure.Status);
        Assert.Equal(1, await verify.EmployeeAttendanceMonthlySummaries.CountAsync(x => x.AttendancePeriodId == period.Id));
        Assert.Equal(before.SourceDataVersion, await verify.EmployeeAttendanceMonthlySummaries.AsNoTracking().Select(x => x.SourceDataVersion).SingleAsync());

        await using var retryDb = database.CreateContext(new TestTenantContext(tenantId));
        var retry = await new AttendanceMonthlyProcessor(retryDb, new TestTenantContext(tenantId)).ProcessAsync(period.Id);
        Assert.True(retry.Succeeded, retry.Message);
        Assert.Equal(1, await retryDb.EmployeeAttendanceMonthlySummaries.CountAsync(x => x.AttendancePeriodId == period.Id));
    }

    [Fact]
    public async Task Ready_period_can_close_reopen_and_reclose_with_append_only_events()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); await SeedTenant(database, tenantId);
        var scope = new TestTenantContext(tenantId);
        await using var context = database.CreateContext(scope);
        var processor = new AttendanceMonthlyProcessor(context, scope);
        var period = (await processor.CreatePeriodAsync(new(2026, 12))).Value!;
        Assert.True((await processor.ProcessAsync(period.Id)).Succeeded);
        var preview = await processor.GetClosePreviewAsync(period.Id);
        Assert.True(preview.Succeeded); Assert.True(preview.Value!.CanClose); Assert.True(preview.Value.SummariesCurrent);

        var closed = await processor.CloseAsync(period.Id);
        Assert.True(closed.Succeeded, closed.Message); Assert.Equal(AttendancePeriodStatus.Closed, closed.Value!.Status);
        var repeatedClose = await processor.CloseAsync(period.Id);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, repeatedClose.Status);

        var reopened = await processor.ReopenAsync(period.Id, new("Correction required for audit."));
        Assert.True(reopened.Succeeded, reopened.Message); Assert.Equal(AttendancePeriodStatus.Open, reopened.Value!.Status); Assert.Equal(2, reopened.Value.DataVersion);
        Assert.True((await processor.ProcessAsync(period.Id)).Succeeded);
        Assert.True((await processor.CloseAsync(period.Id)).Succeeded);

        var events = await context.AttendancePeriodEvents.AsNoTracking().Where(x => x.AttendancePeriodId == period.Id).OrderBy(x => x.OccurredAtUtc).ToListAsync();
        Assert.Equal(new[] { AttendancePeriodEventType.Created, AttendancePeriodEventType.ProcessingStarted, AttendancePeriodEventType.ProcessingCompleted, AttendancePeriodEventType.Closed, AttendancePeriodEventType.Reopened, AttendancePeriodEventType.ProcessingStarted, AttendancePeriodEventType.ProcessingCompleted, AttendancePeriodEventType.Closed }, events.Select(x => x.EventType));
    }

    [Fact]
    public async Task Close_revalidates_stale_summary_and_blocking_exception()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var userId = Guid.NewGuid(); await SeedTenant(database, tenantId);
        await using (var seed = database.CreateContext(new TestTenantContext(tenantId)))
        {
            seed.Users.Add(new User { Id = userId, TenantId = tenantId, Email = "close@test.local", FirstName = "Close", LastName = "Tester" });
            seed.Employees.Add(Employee(tenantId, employeeId, new(2026, 1, 1), null));
            seed.EmployeeEmploymentHistory.Add(History(tenantId, employeeId, new(2026, 1, 1), null));
            await seed.SaveChangesAsync();
        }
        var scope = new TestTenantContext(tenantId, userId);
        await using var context = database.CreateContext(scope);
        var processor = new AttendanceMonthlyProcessor(context, scope);
        var period = (await processor.CreatePeriodAsync(new(2026, 12))).Value!;
        Assert.True((await processor.ProcessAsync(period.Id)).Succeeded);
        var stale = await context.AttendancePeriods.SingleAsync(x => x.Id == period.Id); stale.DataVersion++; await context.SaveChangesAsync();
        var staleClose = await processor.CloseAsync(period.Id);
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, staleClose.Status); Assert.Equal(AttendancePeriodStatus.ReadyToClose, (await context.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id)).Status);

        stale.DataVersion--; await context.SaveChangesAsync();
        var blocked = await processor.GetClosePreviewAsync(period.Id);
        Assert.False(blocked.Value!.CanClose); Assert.Contains(blocked.Value.Blockers, x => x.Contains("Blocking", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, (await processor.CloseAsync(period.Id)).Status);
        Assert.DoesNotContain(await context.AttendancePeriodEvents.AsNoTracking().Where(x => x.AttendancePeriodId == period.Id).ToListAsync(), x => x.EventType == AttendancePeriodEventType.Closed);
    }

    [Fact]
    public async Task Concurrent_close_and_reopen_commands_have_one_winner_and_one_audit_event_each()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); await SeedTenant(database, tenantId);
        await using var setup = database.CreateContext(new TestTenantContext(tenantId));
        var setupProcessor = new AttendanceMonthlyProcessor(setup, new TestTenantContext(tenantId));
        var period = (await setupProcessor.CreatePeriodAsync(new(2026, 10))).Value!;
        Assert.True((await setupProcessor.ProcessAsync(period.Id)).Succeeded);

        await using var closeA = database.CreateContext(new TestTenantContext(tenantId));
        await using var closeB = database.CreateContext(new TestTenantContext(tenantId));
        var closes = await Task.WhenAll(new AttendanceMonthlyProcessor(closeA, new TestTenantContext(tenantId)).CloseAsync(period.Id), new AttendanceMonthlyProcessor(closeB, new TestTenantContext(tenantId)).CloseAsync(period.Id));
        Assert.Single(closes, x => x.Succeeded); Assert.Single(closes, x => !x.Succeeded);

        await using var reopenA = database.CreateContext(new TestTenantContext(tenantId));
        await using var reopenB = database.CreateContext(new TestTenantContext(tenantId));
        var reopens = await Task.WhenAll(new AttendanceMonthlyProcessor(reopenA, new TestTenantContext(tenantId)).ReopenAsync(period.Id, new("Concurrent reopen A")), new AttendanceMonthlyProcessor(reopenB, new TestTenantContext(tenantId)).ReopenAsync(period.Id, new("Concurrent reopen B")));
        Assert.Single(reopens, x => x.Succeeded); Assert.Single(reopens, x => !x.Succeeded);
        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        Assert.Equal(AttendancePeriodStatus.Open, await verify.AttendancePeriods.Where(x => x.Id == period.Id).Select(x => x.Status).SingleAsync());
        Assert.Equal(1, await verify.AttendancePeriodEvents.CountAsync(x => x.AttendancePeriodId == period.Id && x.EventType == AttendancePeriodEventType.Closed));
        Assert.Equal(1, await verify.AttendancePeriodEvents.CountAsync(x => x.AttendancePeriodId == period.Id && x.EventType == AttendancePeriodEventType.Reopened));
    }

    [Fact]
    public async Task Concurrent_process_and_close_leave_a_consistent_period_state()
    {
        using var database = new SqliteInMemoryDatabase();
        var tenantId = Guid.NewGuid(); await SeedTenant(database, tenantId);
        await using var setup = database.CreateContext(new TestTenantContext(tenantId));
        var setupProcessor = new AttendanceMonthlyProcessor(setup, new TestTenantContext(tenantId));
        var period = (await setupProcessor.CreatePeriodAsync(new(2026, 10))).Value!;
        Assert.True((await setupProcessor.ProcessAsync(period.Id)).Succeeded);

        await using var processDb = database.CreateContext(new TestTenantContext(tenantId));
        await using var closeDb = database.CreateContext(new TestTenantContext(tenantId));
        var processTask = new AttendanceMonthlyProcessor(processDb, new TestTenantContext(tenantId)).ProcessAsync(period.Id);
        var closeTask = new AttendanceMonthlyProcessor(closeDb, new TestTenantContext(tenantId)).CloseAsync(period.Id);
        await Task.WhenAll(processTask, closeTask);
        var processResult = await processTask;
        var closeResult = await closeTask;

        await using var verify = database.CreateContext(new TestTenantContext(tenantId));
        var persisted = await verify.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id);
        Assert.NotEqual(AttendancePeriodStatus.Processing, persisted.Status);
        Assert.NotEqual(AttendancePeriodStatus.Open, persisted.Status);
        var closedEvents = await verify.AttendancePeriodEvents.CountAsync(x => x.AttendancePeriodId == period.Id && x.EventType == AttendancePeriodEventType.Closed);
        Assert.InRange(closedEvents, 0, 1);
        if (persisted.Status == AttendancePeriodStatus.Closed)
        {
            Assert.Equal(1, closedEvents);
            var summaryVersions = await verify.EmployeeAttendanceMonthlySummaries.Where(x => x.AttendancePeriodId == period.Id).Select(x => x.SourceDataVersion).ToListAsync();
            Assert.All(summaryVersions, version => Assert.Equal(persisted.DataVersion, version));
        }
        Assert.True(processResult.Succeeded || processResult.Status == HRMS.Application.Common.ResultStatus.Conflict || processResult.Status == HRMS.Application.Common.ResultStatus.ServiceUnavailable);
        Assert.True(closeResult.Succeeded || closeResult.Status == HRMS.Application.Common.ResultStatus.Conflict || closeResult.Status == HRMS.Application.Common.ResultStatus.ServiceUnavailable);
    }

    private sealed class FailSummaryInsertInterceptor : SaveChangesInterceptor
    {
        private int saveCount;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref saveCount) == 2)
                throw new InvalidOperationException("Test-only monthly summary failure.");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static async Task SeedTenant(SqliteInMemoryDatabase database, Guid tenantId)
    {
        await using var catalog = database.CreateCatalogContext();
        await catalog.Tenants.AddAsync(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..20], Host = $"{tenantId:N}.localhost", ShardKey = tenantId.ToString("N") });
        await catalog.SaveChangesAsync();
        await using var context = database.CreateContext(new TestTenantContext(tenantId));
        context.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"T{tenantId:N}"[..20], Host = $"{tenantId:N}.localhost", ShardKey = tenantId.ToString("N") });
        await context.SaveChangesAsync();
    }

    private static Employee Employee(Guid tenant, Guid id, DateOnly joining, DateOnly? leaving) => new() { Id = id, TenantId = tenant, EmployeeCode = $"E{id:N}"[..12], FirstName = "Test", LastName = "Employee", Email = $"{id:N}@test.local", DateOfJoining = joining, DateOfLeaving = leaving };
    private static EmployeeEmploymentHistory History(Guid tenant, Guid employee, DateOnly from, DateOnly? to) => new() { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, EffectiveFrom = from, EffectiveTo = to, EmploymentStatus = EmployeeStatus.Active };
    private static EmployeeAttendanceDay Day(Guid tenant, Guid employee, DateOnly date, EmployeeAttendanceDayStatus status, int? expected, int? actual, bool late = false, bool missingOut = false, bool early = false, bool grace = false, bool missingIn = false) => new() { Id = Guid.NewGuid(), TenantId = tenant, EmployeeId = employee, BusinessDate = date, Status = status, ExpectedWorkMinutes = expected, WorkedMinutes = actual, IsLateIn = late, IsEarlyOut = early, IsGraceApplied = grace, HasMissingInPunch = missingIn, HasMissingOutPunch = missingOut, ProcessedAtUtc = DateTime.UtcNow };
}
