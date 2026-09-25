using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit.Abstractions;

namespace HRMS.Tests;

public sealed class AttendanceOperationsLargeDataTests : IClassFixture<AttendanceOperationsLargeDataTests.LargeDataFixture>
{
    private static readonly DateOnly From = new(2026, 9, 1);
    private static readonly DateOnly To = new(2026, 9, 10);
    private static readonly DateOnly LateDate = new(2026, 9, 6);
    private const int PageSize = 100;
    private readonly LargeDataFixture data;
    private readonly ITestOutputHelper output;

    public AttendanceOperationsLargeDataTests(LargeDataFixture data, ITestOutputHelper output)
    {
        this.data = data;
        this.output = output;
    }

    [Fact]
    public async Task Tenant_operational_query_large_data_is_server_filtered_and_paged()
    {
        var sql = new CapturingCommandInterceptor();
        await using var db = data.Database.CreateContext(new TestTenantContext(data.TenantId, data.ActorId), sql);
        var service = new AttendanceOperationsService(db, new TestTenantContext(data.TenantId, data.ActorId));
        var countClock = Stopwatch.StartNew();
        var result = await service.GetOperationalExceptionsAsync(new() { FromDate = From, ToDate = To, Page = 1, PageSize = PageSize });
        countClock.Stop();
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(49_998, result.Value!.TotalCount);
        Assert.Equal(PageSize, result.Value.Items.Count);
        Assert.All(result.Value.Items, item => Assert.Contains(item.EmployeeId, data.EmployeeIds));
        Assert.DoesNotContain(result.Value.Items, item => item.EmployeeId == data.OtherTenantEmployeeId);
        Assert.Equal(100_000, await db.EmployeeAttendanceDays.CountAsync(x => x.TenantId == data.TenantId));
        var pageSql = sql.Commands.Last(command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("TenantId", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AttendanceExceptionResolutions", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10_000, await db.Employees.CountAsync(x => x.TenantId == data.TenantId));
        Assert.Equal(100_031, await db.EmployeeAttendanceDays.IgnoreQueryFilters().CountAsync());
        output.WriteLine($"FixturePreparation={data.PreparationDuration}; Employees=10000; TenantAttendanceDays=100000; TotalAttendanceDaysAcrossFixtureTenants=100031; DerivedRawExceptions=50000; ActiveTenantExceptions={result.Value.TotalCount}; ExceptionCountAndFirstPage={countClock.Elapsed}; PageSize={PageSize}; SQLServerPagingAndResolutionAntiJoin=PASS");
    }

    [Fact]
    public async Task Manager_scope_large_data_is_applied_before_count_and_page()
    {
        var sql = new CapturingCommandInterceptor();
        await using var db = data.Database.CreateContext(new TestTenantContext(data.TenantId, data.ManagerUserId), sql);
        var service = CreateScopedService(db, data.TenantId, data.ManagerUserId, manager: true);
        var clock = Stopwatch.StartNew();
        var result = await service.GetOperationalExceptionsAsync(new() { FromDate = LateDate, ToDate = LateDate, ExceptionType = AttendanceExceptionType.LateArrival, Page = 1, PageSize = PageSize });
        clock.Stop();
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(100, result.Value!.TotalCount);
        Assert.Equal(PageSize, result.Value.Items.Count);
        Assert.All(result.Value.Items, item => Assert.Contains(item.EmployeeId, data.ManagerEmployeeIds));
        Assert.DoesNotContain(result.Value.Items, item => item.EmployeeId == data.OtherTenantEmployeeId);
        var dashboard = await service.GetDashboardAsync(LateDate, LateDate);
        Assert.True(dashboard.Succeeded, dashboard.Message);
        Assert.Equal(result.Value.TotalCount, dashboard.Value!.LateDays);
        var pageSql = sql.Commands.Last(command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("EmployeeEmploymentHistory", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ManagerId", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AttendanceExceptionResolutions", pageSql, StringComparison.OrdinalIgnoreCase);
        output.WriteLine($"ManagerPage={clock.Elapsed}; Count={result.Value.TotalCount}; Returned={result.Value.Items.Count}; ScopeLeakage=0; DashboardLateParity=PASS; ScopeBeforeCountAndPage=PASS");
    }

    [Fact]
    public async Task HRBP_scope_large_data_is_applied_before_count_and_page()
    {
        var sql = new CapturingCommandInterceptor();
        await using var db = data.Database.CreateContext(new TestTenantContext(data.TenantId, data.HrbpUserId), sql);
        var service = CreateScopedService(db, data.TenantId, data.HrbpUserId);
        var clock = Stopwatch.StartNew();
        var result = await service.GetOperationalExceptionsAsync(new() { FromDate = LateDate, ToDate = LateDate, ExceptionType = AttendanceExceptionType.LateArrival, Page = 1, PageSize = PageSize });
        clock.Stop();
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2_999, result.Value!.TotalCount);
        Assert.Equal(PageSize, result.Value.Items.Count);
        Assert.All(result.Value.Items, item => Assert.Contains(item.EmployeeId, data.HrbpEmployeeIds));
        Assert.DoesNotContain(result.Value.Items, item => item.EmployeeId == data.OtherTenantEmployeeId);
        var pageSql = sql.Commands.Last(command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("EmployeeEmploymentHistory", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DepartmentId", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", pageSql, StringComparison.OrdinalIgnoreCase);
        output.WriteLine($"HRBPPage={clock.Elapsed}; Count={result.Value.TotalCount}; Returned={result.Value.Items.Count}; ScopeLeakage=0; ScopeBeforeCountAndPage=PASS");
    }

    [Fact]
    public async Task TimeManager_scope_large_data_is_applied_before_count_and_page()
    {
        var sql = new CapturingCommandInterceptor();
        await using var db = data.Database.CreateContext(new TestTenantContext(data.TenantId, data.TimeManagerUserId), sql);
        var service = CreateScopedService(db, data.TenantId, data.TimeManagerUserId);
        var clock = Stopwatch.StartNew();
        var result = await service.GetOperationalExceptionsAsync(new() { FromDate = LateDate, ToDate = LateDate, ExceptionType = AttendanceExceptionType.LateArrival, Page = 1, PageSize = PageSize });
        clock.Stop();
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(3_000, result.Value!.TotalCount);
        Assert.Equal(PageSize, result.Value.Items.Count);
        Assert.All(result.Value.Items, item => Assert.Contains(item.EmployeeId, data.TimeManagerEmployeeIds));
        Assert.DoesNotContain(result.Value.Items, item => item.EmployeeId == data.OtherTenantEmployeeId);
        var pageSql = sql.Commands.Last(command => command.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("EmployeeEmploymentHistory", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DepartmentId", pageSql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", pageSql, StringComparison.OrdinalIgnoreCase);
        output.WriteLine($"TimeManagerPage={clock.Elapsed}; Count={result.Value.TotalCount}; Returned={result.Value.Items.Count}; ScopeLeakage=0; ScopeBeforeCountAndPage=PASS");
    }

    [Fact]
    public async Task Dashboard_large_data_counts_match_authoritative_drilldown()
    {
        await using var db = data.Database.CreateContext(new TestTenantContext(data.TenantId, data.ActorId));
        var service = new AttendanceOperationsService(db, new TestTenantContext(data.TenantId, data.ActorId));
        var clock = Stopwatch.StartNew();
        var dashboard = await service.GetDashboardAsync(From, To);
        clock.Stop();
        Assert.True(dashboard.Succeeded, dashboard.Message);
        Assert.Equal(10_000, dashboard.Value!.Employees);
        Assert.Equal(100_000, dashboard.Value.ProcessedDays);
        Assert.Equal(50_000, dashboard.Value.PresentDays);
        Assert.Equal(10_000, dashboard.Value.AbsentDays);
        Assert.Equal(10_000, dashboard.Value.LeaveDays);
        Assert.Equal(10_000, dashboard.Value.OnDutyDays);
        Assert.Equal(10_000, dashboard.Value.WeeklyOffDays);
        Assert.Equal(10_000, dashboard.Value.HolidayDays);
        Assert.Equal(50_000, dashboard.Value.ExceptionDays);
        Assert.Equal(9_999, dashboard.Value.LateDays);
        Assert.Equal(9_999, dashboard.Value.EarlyDepartureDays);
        Assert.Equal(20_000, dashboard.Value.MissedPunchExceptions);
        Assert.Equal(6_000, dashboard.Value.PendingCorrections);
        Assert.Equal(10_000, dashboard.Value.ActiveAbsentExceptions);
        Assert.Equal(10_000, await Count(AttendanceExceptionType.Absent));
        Assert.Equal(9_999, await Count(AttendanceExceptionType.LateArrival));
        Assert.Equal(9_999, await Count(AttendanceExceptionType.EarlyDeparture));
        Assert.Equal(10_000, await Count(AttendanceExceptionType.MissingInPunch));
        Assert.Equal(10_000, await Count(AttendanceExceptionType.MissingOutPunch));
        Assert.Equal(dashboard.Value.PendingCorrections, await db.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == data.TenantId && x.BusinessDate >= From && x.BusinessDate <= To && x.Status == AttendanceRequestStatus.Pending));
        output.WriteLine($"Dashboard={clock.Elapsed}; DashboardDrilldownParity=PASS; Counts=Present:50000,Absent:10000,Leave:10000,OD:10000,Late:9999,Early:9999,MissedPunch:20000,PendingCorrection:6000,RawExceptionFacts:50000");

        async Task<int> Count(AttendanceExceptionType type)
        {
            var rows = await service.GetOperationalExceptionsAsync(new() { FromDate = From, ToDate = To, ExceptionType = type, Page = 1, PageSize = 1 });
            Assert.True(rows.Succeeded, rows.Message);
            return rows.Value!.TotalCount;
        }
    }

    [Fact]
    public async Task Bulk_large_data_is_scoped_audited_idempotent_and_conflict_safe()
    {
        const int bulkCount = 1_000;
        await using (var db = data.Database.CreateContext(new TestTenantContext(data.TenantId, data.ActorId)))
        {
            var service = new AttendanceOperationsService(db, new TestTenantContext(data.TenantId, data.ActorId));
            var clock = Stopwatch.StartNew();
            for (var offset = 0; offset < bulkCount; offset += 100)
            {
                var items = Enumerable.Range(offset, Math.Min(100, bulkCount - offset)).Select(i => new AttendanceBulkCorrectionItem(data.EmployeeIds[6_000 + i], new DateOnly(2026, 9, 2), AttendanceRegularizationType.CorrectInOutTime, new DateTime(2026, 9, 2, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 2, 18, 0, 0, DateTimeKind.Utc), "Large-data bounded correction", 1)).ToArray();
                var result = await service.ApplyBulkCorrectionsAsync(items);
                Assert.True(result.Succeeded, result.Message);
                Assert.Equal(items.Length, result.Value!.Items.Count);
                Assert.Equal(items.Length, result.Value.Succeeded);
                Assert.All(result.Value.Items, item => Assert.True(item.Success));
            }
            clock.Stop();
            Assert.Equal(6_000, await db.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == data.TenantId && x.Status == AttendanceRequestStatus.Pending && x.BusinessDate == From));
            Assert.Equal(bulkCount, await db.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == data.TenantId && x.Status == AttendanceRequestStatus.Pending && x.BusinessDate == new DateOnly(2026, 9, 2)));
            var submittedAudits = await db.EmployeeAuditLogs.Where(x => x.TenantId == data.TenantId && x.FieldName == "ManualAttendanceRequested" && x.EffectiveDate == new DateOnly(2026, 9, 2)).GroupBy(x => x.ImportBatchId).Select(group => group.Count()).ToListAsync();
            Assert.Equal(bulkCount, submittedAudits.Count);
            Assert.All(submittedAudits, count => Assert.Equal(1, count));
            Assert.Equal(0, await db.AttendanceAdjustments.CountAsync(x => x.TenantId == data.TenantId && x.BusinessDate == new DateOnly(2026, 9, 2)));
            output.WriteLine($"Bulk={clock.Elapsed}; Items={bulkCount}; PerItemResults=PASS; DuplicateAuditEffects=0; DuplicateCorrections=0");
        }

        await using (var db = data.Database.CreateContext(new TestTenantContext(data.TenantId, data.ManagerUserId)))
        {
            var manager = CreateScopedService(db, data.TenantId, data.ManagerUserId, manager: true);
            var scopedItems = new[]
            {
                Correction(data.EmployeeIds[99], new DateOnly(2026, 9, 10)),
                Correction(data.EmployeeIds[9_000], new DateOnly(2026, 9, 10)),
                Correction(data.EmployeeIds[98], new DateOnly(2026, 9, 10), expectedVersion: 2)
            };
            var scoped = await manager.ApplyBulkCorrectionsAsync(scopedItems);
            Assert.True(scoped.Succeeded, scoped.Message);
            Assert.True(scoped.Value!.Items[0].Success);
            Assert.False(scoped.Value.Items[1].Success);
            Assert.Equal("Unauthorized", scoped.Value.Items[1].FailureCode);
            Assert.False(scoped.Value.Items[2].Success);
            Assert.Equal("StaleVersion", scoped.Value.Items[2].FailureCode);
            Assert.Equal(0, await db.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == data.TenantId && x.EmployeeId == data.EmployeeIds[9_000] && x.BusinessDate == new DateOnly(2026, 9, 10)));
            Assert.Equal(0, await db.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == data.TenantId && x.EmployeeId == data.EmployeeIds[98] && x.BusinessDate == new DateOnly(2026, 9, 10)));
            Assert.Equal(0, await db.EmployeeAuditLogs.CountAsync(x => x.TenantId == data.TenantId && (x.EmployeeId == data.EmployeeIds[9_000] || x.EmployeeId == data.EmployeeIds[98]) && x.EffectiveDate == new DateOnly(2026, 9, 10) && x.FieldName == "ManualAttendanceRequested"));
        }

        var raceEmployee = data.EmployeeIds[8_000];
        var coordinator = new SqliteSerializationCoordinator();
        var startRace = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var raceTasks = new[]
        {
            RaceSubmit(raceEmployee, "race A", new TimeOnly(9, 5), coordinator, startRace.Task),
            RaceSubmit(raceEmployee, "race B", new TimeOnly(9, 10), coordinator, startRace.Task)
        };
        startRace.SetResult();
        var race = await Task.WhenAll(raceTasks);
        Assert.Single(race.Where(result => result.Succeeded));
        Assert.Single(race.Where(result => !result.Succeeded));
        await using var verify = data.Database.CreateContext(new TestTenantContext(data.TenantId, data.ActorId));
        Assert.Equal(1, await verify.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == data.TenantId && x.EmployeeId == raceEmployee && x.BusinessDate == new DateOnly(2026, 9, 10)));
        Assert.Equal(1, await verify.EmployeeAuditLogs.CountAsync(x => x.TenantId == data.TenantId && x.EmployeeId == raceEmployee && x.FieldName == "ManualAttendanceRequested" && x.EffectiveDate == new DateOnly(2026, 9, 10)));
        Assert.Equal(7_004, await verify.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == data.TenantId));
        output.WriteLine("LargeFixtureCorrectionRace=PASS; LostUpdates=0; DuplicateCorrectionEffects=0; DuplicateAuditEffects=0; CrossEmployeeMutation=0");

        static AttendanceBulkCorrectionItem Correction(Guid employeeId, DateOnly date, int expectedVersion = 1) => new(employeeId, date, AttendanceRegularizationType.CorrectInOutTime, date.ToDateTime(new(9, 0), DateTimeKind.Utc), date.ToDateTime(new(18, 0), DateTimeKind.Utc), "Scoped large-data bulk", expectedVersion);
        async Task<HRMS.Application.Common.Result<RegularizationDto>> RaceSubmit(Guid employeeId, string reason, TimeOnly start, SqliteSerializationCoordinator lockCoordinator, Task startGate)
        {
            var tenant = new TestTenantContext(data.TenantId, data.ActorId);
            await using var context = data.Database.CreateIsolatedContext(tenant, lockCoordinator);
            var submitter = new AttendanceOperationsService(context, tenant, employeeLock: lockCoordinator);
            var input = new ManualAttendanceRequest(employeeId, new DateOnly(2026, 9, 10), AttendanceRegularizationType.CorrectInOutTime, new DateOnly(2026, 9, 10).ToDateTime(start, DateTimeKind.Utc), new DateOnly(2026, 9, 10).ToDateTime(new(18, 0), DateTimeKind.Utc), reason, null, 1);
            return await Task.Run(async () => { await startGate; return await submitter.SubmitManualAttendanceAsync(input); });
        }
    }

    [Fact]
    public async Task Finalized_period_large_data_lifecycle_preserves_history_and_blocks_locked_correction()
    {
        var tenantId = data.FinalizedTenantId;
        var makerId = data.FinalizedMakerId;
        var checkerId = data.FinalizedCheckerId;
        var employeeId = data.FinalizedEmployeeId;
        var tenant = new TestTenantContext(tenantId, makerId);
        await using var db = data.Database.CreateContext(tenant);
        var processor = new AttendanceMonthlyProcessor(db, tenant);
        var existingPeriods = await db.AttendancePeriods.AsNoTracking().Where(x => x.TenantId == tenantId && x.Year == 2026 && x.Month == 9).ToListAsync();
        var makerExists = await db.Users.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenantId && x.Id == makerId);
        output.WriteLine($"FinalizationFixtureExistingPeriods={existingPeriods.Count}; MakerPrincipalExists={makerExists}");
        Assert.Empty(existingPeriods);
        Assert.True(makerExists);
        var create = await processor.CreatePeriodAsync(new(2026, 9));
        Assert.True(create.Succeeded, create.Message);
        var periodId = create.Value!.Id;
        var process = await processor.ProcessAsync(periodId);
        Assert.True(process.Succeeded, process.Message);
        var finalizeClock = Stopwatch.StartNew();
        var close = await processor.CloseAsync(create.Value.Id);
        finalizeClock.Stop();
        Assert.True(close.Succeeded, close.Message);
        var v1 = await db.PayrollAttendanceSnapshots.SingleAsync(x => x.TenantId == tenantId && x.AttendancePeriodId == periodId && x.IsCurrent);
        var v1Id = v1.Id;
        var v1Hash = v1.SourceHash;
        Assert.Equal(1, v1.Version);
        Assert.Equal(1, await db.PayrollAttendanceSnapshots.CountAsync(x => x.TenantId == tenantId && x.AttendancePeriodId == periodId && x.IsCurrent));

        var lockedService = new AttendanceOperationsService(db, tenant);
        var dayBefore = await db.EmployeeAttendanceDays.AsNoTracking().SingleAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.BusinessDate == From);
        var rejected = await lockedService.SubmitManualAttendanceAsync(new(employeeId, From, AttendanceRegularizationType.CorrectInOutTime, From.ToDateTime(new(9, 10), DateTimeKind.Utc), From.ToDateTime(new(18, 0), DateTimeKind.Utc), "locked mutation attempt", null, 1));
        Assert.False(rejected.Succeeded);
        Assert.Contains("finalized", rejected.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(dayBefore.Status, await db.EmployeeAttendanceDays.Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.BusinessDate == From).Select(x => x.Status).SingleAsync());
        Assert.Equal(v1Hash, await db.PayrollAttendanceSnapshots.Where(x => x.Id == v1Id).Select(x => x.SourceHash).SingleAsync());
        Assert.Equal(0, await db.EmployeeAuditLogs.CountAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Reason == "locked mutation attempt"));
        Assert.Equal(0, await db.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.Reason == "locked mutation attempt"));

        var reopenClock = Stopwatch.StartNew();
        var reopen = await processor.ReopenAsync(periodId, new("Large-data controlled reopen"));
        Assert.True(reopen.Succeeded, reopen.Message);
        var reopenedVersion = await db.AttendancePeriods.Where(x => x.Id == create.Value.Id).Select(x => x.DataVersion).SingleAsync();
        var request = await new AttendanceOperationsService(db, tenant).SubmitManualAttendanceAsync(new(employeeId, From, AttendanceRegularizationType.CorrectInOutTime, From.ToDateTime(new(9, 5), DateTimeKind.Utc), From.ToDateTime(new(18, 0), DateTimeKind.Utc), "Large-data versioned correction", null, reopenedVersion));
        Assert.True(request.Succeeded, request.Message);
        var checker = new TestTenantContext(tenantId, checkerId);
        var review = await new AttendanceOperationsService(db, checker).ApplyBulkActionAsync([new(request.Value!.Id, false, true, 1, "Approved after reopen")]);
        Assert.True(review.Succeeded, review.Message);
        Assert.True(review.Value!.Items.Single().Success, review.Value.Items.Single().Message);
        var processAgain = await processor.ProcessAsync(periodId);
        Assert.True(processAgain.Succeeded, processAgain.Message);
        var refinalized = await processor.CloseAsync(periodId);
        reopenClock.Stop();
        Assert.True(refinalized.Succeeded, refinalized.Message);
        Assert.True(await db.PayrollAttendanceSnapshots.AnyAsync(x => x.Id == v1Id && x.Version == 1 && !x.IsCurrent));
        Assert.Equal(v1Hash, await db.PayrollAttendanceSnapshots.Where(x => x.Id == v1Id).Select(x => x.SourceHash).SingleAsync());
        Assert.Equal(2, await db.PayrollAttendanceSnapshots.CountAsync(x => x.TenantId == tenantId && x.AttendancePeriodId == periodId));
        Assert.Equal(1, await db.PayrollAttendanceSnapshots.CountAsync(x => x.TenantId == tenantId && x.AttendancePeriodId == periodId && x.IsCurrent));
        Assert.Equal(2, await db.PayrollAttendanceSnapshots.Where(x => x.TenantId == tenantId && x.AttendancePeriodId == periodId && x.IsCurrent).Select(x => x.Version).SingleAsync());
        Assert.Equal(1, await db.AttendanceRegularizationRequests.CountAsync(x => x.TenantId == tenantId && x.EmployeeId == employeeId && x.BusinessDate == From && x.Reason == "Large-data versioned correction"));
        output.WriteLine($"Finalization={finalizeClock.Elapsed}; ReopenCorrectRefinalize={reopenClock.Elapsed}; V1Preserved=PASS; V2Current=PASS; CurrentSnapshots=1; UnauthorizedFinalizedMutations=0; CrossTenantMutation=0");
    }

    [Fact]
    public async Task Export_large_data_is_capped_paged_and_respects_operational_scopes()
    {
        await using var db = data.Database.CreateContext(new TestTenantContext(data.TenantId, data.ActorId));
        var tenantService = new AttendanceOperationsService(db, new TestTenantContext(data.TenantId, data.ActorId));
        var tenantClock = Stopwatch.StartNew();
        var tenantExport = await tenantService.ExportExceptionsAsync(new() { FromDate = From, ToDate = To });
        tenantClock.Stop();
        Assert.True(tenantExport.Succeeded, tenantExport.Message);
        Assert.Equal(49_998, tenantExport.Value!.RowCount);
        Assert.DoesNotContain(data.OtherTenantEmployeeId.ToString(), System.Text.Encoding.UTF8.GetString(tenantExport.Value.Content));

        var managerClock = Stopwatch.StartNew();
        var manager = await ExportScoped(data.ManagerUserId, true);
        managerClock.Stop();
        Assert.Equal(100, manager.RowCount);
        var hrbpClock = Stopwatch.StartNew();
        var hrbp = await ExportScoped(data.HrbpUserId);
        hrbpClock.Stop();
        Assert.Equal(2_999, hrbp.RowCount);
        var timeManagerClock = Stopwatch.StartNew();
        var timeManager = await ExportScoped(data.TimeManagerUserId);
        timeManagerClock.Stop();
        Assert.Equal(3_000, timeManager.RowCount);
        var managerCsv = System.Text.Encoding.UTF8.GetString(manager.Content);
        var hrbpCsv = System.Text.Encoding.UTF8.GetString(hrbp.Content);
        var timeManagerCsv = System.Text.Encoding.UTF8.GetString(timeManager.Content);
        Assert.DoesNotContain("6E-OTHER", managerCsv);
        Assert.DoesNotContain("6E-OTHER", hrbpCsv);
        Assert.DoesNotContain("6E-OTHER", timeManagerCsv);
        Assert.DoesNotContain("6E-L-09000", managerCsv);
        Assert.DoesNotContain("6E-L-09000", hrbpCsv);
        Assert.DoesNotContain("6E-L-00100", timeManagerCsv);
        output.WriteLine($"TenantWideExport={tenantClock.Elapsed}; ScopedExports=Manager:{managerClock.Elapsed},HRBP:{hrbpClock.Elapsed},TimeManager:{timeManagerClock.Elapsed}; ExportRows=49998; OutsideScopeRows=0; Mechanism=50,000-row cap with repeated server-paged workbench queries");

        async Task<AttendanceOperationsExport> ExportScoped(Guid userId, bool managerScope = false)
        {
            await using var scopedDb = data.Database.CreateContext(new TestTenantContext(data.TenantId, userId));
            var scopedService = CreateScopedService(scopedDb, data.TenantId, userId, managerScope);
            var result = await scopedService.ExportExceptionsAsync(new() { FromDate = LateDate, ToDate = LateDate, ExceptionType = AttendanceExceptionType.LateArrival });
            Assert.True(result.Succeeded, result.Message);
            return result.Value!;
        }
    }

    private static AttendanceOperationsService CreateScopedService(HrmsDbContext db, Guid tenantId, Guid userId, bool manager = false)
    {
        var tenant = new TestTenantContext(tenantId, userId);
        var current = new TestAuthorizationContext(Permissions.Attendance.ExceptionView, Permissions.Attendance.RegularizationApprove, Permissions.Attendance.AdminCorrectionManage, manager ? Permissions.Attendance.MonthlyViewTeam : "");
        var scopes = new EmployeeAccessScopeService(db, tenant, current);
        var authorization = new AttendanceAuthorizationService(db, tenant, new EmployeeIdentityResolver(db, tenant), scopes, new EmployeeManagerResolver(db, tenant), current);
        return new AttendanceOperationsService(db, tenant, authorization);
    }

    public sealed class LargeDataFixture : IAsyncLifetime
    {
        public const int EmployeeCount = 10_000;
        public const int DaysPerEmployee = 10;
        public const int InitialPendingCorrections = 6_000;
        public readonly SqliteInMemoryDatabase Database = new();
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly Guid OtherTenantId = Guid.NewGuid();
        public readonly Guid ActorId = Guid.NewGuid();
        public readonly Guid ManagerUserId = Guid.NewGuid();
        public readonly Guid HrbpUserId = Guid.NewGuid();
        public readonly Guid TimeManagerUserId = Guid.NewGuid();
        public readonly Guid ManagerEmployeeId;
        public readonly Guid[] EmployeeIds = Enumerable.Range(0, EmployeeCount).Select(i => GuidFrom(i + 1)).ToArray();
        public readonly Guid[] ManagerEmployeeIds;
        public readonly Guid[] HrbpEmployeeIds;
        public readonly Guid[] TimeManagerEmployeeIds;
        public readonly Guid DepartmentA = Guid.NewGuid();
        public readonly Guid DepartmentB = Guid.NewGuid();
        public readonly Guid DepartmentC = Guid.NewGuid();
        public readonly Guid OtherTenantEmployeeId = Guid.NewGuid();
        public readonly Guid FinalizedTenantId = Guid.NewGuid();
        public readonly Guid FinalizedMakerId = Guid.NewGuid();
        public readonly Guid FinalizedCheckerId = Guid.NewGuid();
        public readonly Guid FinalizedEmployeeId = Guid.NewGuid();
        public TimeSpan PreparationDuration { get; private set; }

        public LargeDataFixture()
        {
            ManagerEmployeeId = EmployeeIds[^1];
            ManagerEmployeeIds = [.. EmployeeIds.Take(100), ManagerEmployeeId];
            HrbpEmployeeIds = [.. EmployeeIds.Take(3_000)];
            TimeManagerEmployeeIds = [.. EmployeeIds.Skip(3_000).Take(3_000)];
        }

        public async Task InitializeAsync()
        {
            var prep = Stopwatch.StartNew();
            var tenantCode = $"L{TenantId:N}"[..12];
            await using (var seed = Database.CreateContext(new TestTenantContext(TenantId, ActorId)))
            {
                seed.Tenants.AddRange(
                    new Tenant { Id = TenantId, TenantCode = tenantCode, TenantName = "6E Operations Large", Host = $"{TenantId:N}.test", ShardKey = TenantId.ToString("N") },
                    new Tenant { Id = OtherTenantId, TenantCode = $"X{OtherTenantId:N}"[..12], TenantName = "6E Other Tenant", Host = $"{OtherTenantId:N}.test", ShardKey = OtherTenantId.ToString("N") },
                    new Tenant { Id = FinalizedTenantId, TenantCode = $"F{FinalizedTenantId:N}"[..12], TenantName = "6E Finalized Period", Host = $"{FinalizedTenantId:N}.test", ShardKey = FinalizedTenantId.ToString("N") });
                seed.Users.AddRange(
                    NewUser(ActorId, TenantId, "Large", "Operator"),
                    NewUser(ManagerUserId, TenantId, "Scoped", "Manager"),
                    NewUser(HrbpUserId, TenantId, "Scoped", "HRBP"),
                    NewUser(TimeManagerUserId, TenantId, "Scoped", "TimeManager"));
                seed.Roles.AddRange(new Role { Id = SeedData.RoleId(RoleNames.Manager), Name = RoleNames.Manager }, new Role { Id = SeedData.RoleId(RoleNames.HRBP), Name = RoleNames.HRBP }, new Role { Id = SeedData.RoleId(RoleNames.TimeManager), Name = RoleNames.TimeManager });
                await seed.SaveChangesAsync();
                seed.Departments.AddRange(
                    new Department { Id = DepartmentA, TenantId = TenantId, Code = "6E-L-A", Name = "Large HRBP Scope A" },
                    new Department { Id = DepartmentB, TenantId = TenantId, Code = "6E-L-B", Name = "Large Time Manager Scope B" },
                    new Department { Id = DepartmentC, TenantId = TenantId, Code = "6E-L-C", Name = "Large Out Of Scope C" });
                await seed.SaveChangesAsync();
                seed.Employees.AddRange(Enumerable.Range(0, EmployeeCount).Select(i => new Employee
                {
                    Id = EmployeeIds[i], TenantId = TenantId, EmployeeCode = $"6E-L-{i:D5}", FirstName = "Employee", LastName = i.ToString("D5"),
                    Email = $"6e-large-{i:D5}@example.test", DateOfJoining = new DateOnly(2026, 1, 1), Status = EmployeeStatus.Active
                }));
                await seed.SaveChangesAsync();
                AddAccountLink(seed, ManagerUserId, ManagerEmployeeId);
                AddAccountLink(seed, HrbpUserId, EmployeeIds[9_998]);
                AddAccountLink(seed, TimeManagerUserId, EmployeeIds[9_997]);
                var hrbpAssignment = Assignment(HrbpUserId, SeedData.RoleId(RoleNames.HRBP));
                hrbpAssignment.Scopes.Add(new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = TenantId, UserRoleAssignmentId = hrbpAssignment.Id, ScopeType = RoleScopeType.Department, ScopeEntityId = DepartmentA });
                var timeManagerAssignment = Assignment(TimeManagerUserId, SeedData.RoleId(RoleNames.TimeManager));
                timeManagerAssignment.Scopes.Add(new UserRoleAssignmentScope { Id = Guid.NewGuid(), TenantId = TenantId, UserRoleAssignmentId = timeManagerAssignment.Id, ScopeType = RoleScopeType.Department, ScopeEntityId = DepartmentB });
                seed.UserRoles.AddRange(Assignment(ManagerUserId, SeedData.RoleId(RoleNames.Manager)), hrbpAssignment, timeManagerAssignment);
                await seed.SaveChangesAsync();
                seed.EmployeeEmploymentHistory.AddRange(Enumerable.Range(0, EmployeeCount).Select(i => new EmployeeEmploymentHistory
                {
                    Id = GuidFrom(500_000 + i), TenantId = TenantId, EmployeeId = EmployeeIds[i], EffectiveFrom = new DateOnly(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active,
                    DepartmentId = i < 3_000 ? DepartmentA : i < 6_000 ? DepartmentB : DepartmentC,
                    ManagerId = i < 100 ? ManagerEmployeeId : null
                }));
                var batch = new List<EmployeeAttendanceDay>(10_000);
                for (var i = 0; i < EmployeeCount; i++)
                for (var d = 0; d < DaysPerEmployee; d++)
                {
                    var date = new DateOnly(2026, 9, 1).AddDays(d);
                    batch.Add(new EmployeeAttendanceDay
                    {
                        Id = DayId(i, d), TenantId = TenantId, EmployeeId = EmployeeIds[i], BusinessDate = date,
                        Status = d == 0 ? EmployeeAttendanceDayStatus.Absent : d == 1 ? EmployeeAttendanceDayStatus.OnLeave : d == 2 ? EmployeeAttendanceDayStatus.OnDuty : d == 3 ? EmployeeAttendanceDayStatus.WeeklyOff : d == 4 ? EmployeeAttendanceDayStatus.Holiday : EmployeeAttendanceDayStatus.Present,
                        ExpectedWorkMinutes = 480, WorkedMinutes = d == 0 ? 0 : 480, ProcessedAtUtc = DateTime.UtcNow,
                        IsLateIn = d == 5, IsEarlyOut = d == 6, HasMissingInPunch = d == 7, HasMissingOutPunch = d == 8,
                        ScheduledStartUtc = date.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc), ScheduledEndUtc = date.ToDateTime(new TimeOnly(18, 0), DateTimeKind.Utc),
                        FirstPunchAtUtc = date.ToDateTime(new TimeOnly(d == 5 ? 9 : 9, 30), DateTimeKind.Utc), LastPunchAtUtc = date.ToDateTime(new TimeOnly(d == 6 ? 17 : 18, 0), DateTimeKind.Utc)
                    });
                    if (batch.Count == 10_000) { seed.EmployeeAttendanceDays.AddRange(batch); await seed.SaveChangesAsync(); seed.ChangeTracker.Clear(); batch.Clear(); }
                }
                seed.EmployeeAttendanceDays.AddRange(batch);
                await seed.SaveChangesAsync();
                seed.AttendanceRegularizationRequests.AddRange(Enumerable.Range(0, InitialPendingCorrections).Select(i => new AttendanceRegularizationRequest
                {
                    Id = GuidFrom(300_000 + i), TenantId = TenantId, EmployeeId = EmployeeIds[i], BusinessDate = From,
                    RequestType = AttendanceRegularizationType.CorrectInOutTime, Reason = "Large-data pending correction", Status = AttendanceRequestStatus.Pending,
                    SubmittedByUserId = ActorId, SubmittedAtUtc = DateTime.UtcNow, ConcurrencyVersion = 1
                }));
                var approvedId = GuidFrom(400_000);
                var rejectedId = GuidFrom(400_001);
                seed.AttendanceRegularizationRequests.AddRange(
                    new AttendanceRegularizationRequest { Id = approvedId, TenantId = TenantId, EmployeeId = EmployeeIds[7_000], BusinessDate = new DateOnly(2026, 9, 3), RequestType = AttendanceRegularizationType.CorrectInOutTime, Reason = "Approved manual Attendance example", Status = AttendanceRequestStatus.Approved, SubmittedByUserId = ActorId, ReviewedByUserId = ManagerUserId, SubmittedAtUtc = DateTime.UtcNow, ReviewedAtUtc = DateTime.UtcNow, ConcurrencyVersion = 2 },
                    new AttendanceRegularizationRequest { Id = rejectedId, TenantId = TenantId, EmployeeId = EmployeeIds[7_001], BusinessDate = new DateOnly(2026, 9, 3), RequestType = AttendanceRegularizationType.CorrectInOutTime, Reason = "Rejected correction example", Status = AttendanceRequestStatus.Rejected, SubmittedByUserId = ActorId, ReviewedByUserId = ManagerUserId, SubmittedAtUtc = DateTime.UtcNow, ReviewedAtUtc = DateTime.UtcNow, ConcurrencyVersion = 2 });
                await seed.SaveChangesAsync();
                seed.AttendanceAdjustments.Add(new AttendanceAdjustment { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeIds[7_000], BusinessDate = new DateOnly(2026, 9, 3), AttendanceRegularizationRequestId = approvedId, EffectiveInAtUtc = new DateTime(2026, 9, 3, 9, 0, 0, DateTimeKind.Utc), EffectiveOutAtUtc = new DateTime(2026, 9, 3, 18, 0, 0, DateTimeKind.Utc), ApprovedByUserId = ManagerUserId, ApprovedAtUtc = DateTime.UtcNow });
                seed.AttendanceExceptionResolutions.AddRange(
                    Resolution(EmployeeIds[0], DayId(0, 5), AttendanceExceptionType.LateArrival, AttendanceExceptionResolutionAction.Acknowledge, ManagerUserId),
                    Resolution(EmployeeIds[0], DayId(0, 6), AttendanceExceptionType.EarlyDeparture, AttendanceExceptionResolutionAction.Waive, ManagerUserId));
                await seed.SaveChangesAsync();
            }
            await using (var otherTenant = Database.CreateContext(new TestTenantContext(OtherTenantId)))
            {
                otherTenant.Employees.Add(new Employee { Id = OtherTenantEmployeeId, TenantId = OtherTenantId, EmployeeCode = "6E-OTHER", FirstName = "Other", LastName = "Tenant", Email = "6e-other@example.test", DateOfJoining = From, Status = EmployeeStatus.Active });
                otherTenant.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = OtherTenantId, EmployeeId = OtherTenantEmployeeId, BusinessDate = From, Status = EmployeeAttendanceDayStatus.Absent, ProcessedAtUtc = DateTime.UtcNow });
                await otherTenant.SaveChangesAsync();
            }
            await SeedFinalizationTenantAsync();
            prep.Stop();
            PreparationDuration = prep.Elapsed;
        }

        public Task DisposeAsync() { Database.Dispose(); return Task.CompletedTask; }

        private async Task SeedFinalizationTenantAsync()
        {
            await using var seed = Database.CreateContext(new TestTenantContext(FinalizedTenantId, FinalizedMakerId));
            seed.Users.AddRange(NewUser(FinalizedMakerId, FinalizedTenantId, "Finalization", "Maker"), NewUser(FinalizedCheckerId, FinalizedTenantId, "Finalization", "Checker"));
            await seed.SaveChangesAsync();
            seed.Employees.Add(new Employee { Id = FinalizedEmployeeId, TenantId = FinalizedTenantId, EmployeeCode = "6E-FINAL-1", FirstName = "Finalized", LastName = "Fixture", Email = "6e-finalized@example.test", DateOfJoining = From, Status = EmployeeStatus.Active });
            seed.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = FinalizedTenantId, EmployeeId = FinalizedEmployeeId, EffectiveFrom = new DateOnly(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            for (var day = From; day <= new DateOnly(2026, 9, 30); day = day.AddDays(1)) seed.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = FinalizedTenantId, EmployeeId = FinalizedEmployeeId, BusinessDate = day, Status = EmployeeAttendanceDayStatus.Present, ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        private static User NewUser(Guid id, Guid tenantId, string first, string last) => new() { Id = id, TenantId = tenantId, Email = $"{id:N}@example.test", FirstName = first, LastName = last, IsActive = true };
        private void AddAccountLink(HrmsDbContext db, Guid userId, Guid employeeId)
        {
            var linkId = Guid.NewGuid();
            db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
            {
                Id = linkId, TenantId = TenantId, SubjectUserId = userId, ActorUserId = ActorId, Sequence = 1,
                Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow,
                Reason = "Deterministic large-data authorization fixture", CorrelationId = linkId.ToString("N")
            });
            db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = TenantId, UserId = userId, EmployeeId = employeeId });
        }
        private UserRole Assignment(Guid userId, int roleId) => new() { Id = Guid.NewGuid(), TenantId = TenantId, UserId = userId, RoleId = roleId, EffectiveFrom = new DateOnly(2026, 1, 1), AssignmentSource = RoleAssignmentSource.System };
        private AttendanceExceptionResolution Resolution(Guid employeeId, Guid dayId, AttendanceExceptionType type, AttendanceExceptionResolutionAction action, Guid actorId) => new() { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = employeeId, AttendanceDayId = dayId, AttendanceVersion = 1, ExceptionType = type, Action = action, Reason = "Large-data resolved example", ResolvedBy = actorId, ResolvedAtUtc = DateTime.UtcNow };
        private static Guid DayId(int employeeIndex, int dayIndex) => GuidFrom(100_000 + employeeIndex * DaysPerEmployee + dayIndex);
    }

    private sealed class TestAuthorizationContext(params string[] permissions) : ICurrentAuthorizationContext
    {
        private readonly HashSet<string> granted = permissions.Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal);
        public bool HasAnyPermission(params string[] permissions) => permissions.Any(granted.Contains);
    }

    private sealed class CapturingCommandInterceptor : DbCommandInterceptor
    {
        private readonly ConcurrentQueue<string> commands = new();
        public IReadOnlyCollection<string> Commands => commands.ToArray();
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result) { commands.Enqueue(command.CommandText); return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default) { commands.Enqueue(command.CommandText); return ValueTask.FromResult(result); }
    }

    private sealed class SqliteSerializationCoordinator : DbTransactionInterceptor, IEmployeeSerializationLock
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) => gate.WaitAsync(cancellationToken);
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { gate.Release(); return base.TransactionCommittedAsync(transaction, eventData, cancellationToken); }
        public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) { gate.Release(); return base.TransactionRolledBackAsync(transaction, eventData, cancellationToken); }
    }

    private static Guid GuidFrom(int value) => new(value, 0, 0, new byte[8]);
}
