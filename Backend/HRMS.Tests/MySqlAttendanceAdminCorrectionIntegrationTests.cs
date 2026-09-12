using System.Net;
using System.Net.Http.Json;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Sdk;

namespace HRMS.Tests;

[Collection("Attendance MySQL")]
public sealed class MySqlAttendanceAdminCorrectionIntegrationTests
{
    [Fact]
    public async Task MySql_admin_correction_persists_and_reprocesses_effective_attendance()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("Phase 5D MySQL test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            var date = new DateOnly(2026, 10, 5);
            await using var db = fixture.CreateContext(fixture.EmployeeTenant);
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"AC-{fixture.TenantId:N}"[..10], ShiftName = "Correction", IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), FullDayWorkMinutes = 480, PlannedDurationMinutes = 540, MinimumWorkMinutes = 480 };
            db.Shifts.Add(shift);
            db.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = "Correction employee", EmployeeId = fixture.EmployeeId, ShiftId = shift.Id, Priority = 100, EffectiveFrom = new(2026, 1, 1) });
            await db.SaveChangesAsync();
            var roster = new AttendanceFoundationService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant));
            var dayProcessor = new AttendanceDayProcessor(db, fixture.EmployeeTenant, roster, TimeProvider.System, new AttendancePeriodLockService(db, fixture.EmployeeTenant));
            var service = new AttendanceAdminCorrectionService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant), dayProcessor, new AttendancePeriodLockService(db, fixture.EmployeeTenant));
            var result = await service.CreateAsync(new(fixture.EmployeeId, date, new DateTime(2026, 10, 5, 9, 15, 0, DateTimeKind.Utc), new DateTime(2026, 10, 5, 17, 15, 0, DateTimeKind.Utc), "MySQL admin correction"));
            Assert.True(result.Succeeded, result.Message);
            var correction = await db.AttendanceAdminCorrections.AsNoTracking().SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date);
            var day = await db.EmployeeAttendanceDays.AsNoTracking().SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date);
            Assert.Equal("MySQL admin correction", correction.Reason);
            Assert.Equal(new DateTime(2026, 10, 5, 9, 15, 0, DateTimeKind.Utc), day.FirstPunchAtUtc);
            Assert.Equal(new DateTime(2026, 10, 5, 17, 15, 0, DateTimeKind.Utc), day.LastPunchAtUtc);

            var second = await service.CreateAsync(new(fixture.EmployeeId, date, new DateTime(2026, 10, 5, 8, 45, 0, DateTimeKind.Utc), new DateTime(2026, 10, 5, 16, 45, 0, DateTimeKind.Utc), "MySQL admin correction v2"));
            Assert.True(second.Succeeded, second.Message);
            await using var verify = fixture.CreateContext(fixture.EmployeeTenant);
            var history = await verify.AttendanceAdminCorrections.AsNoTracking().Where(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date).OrderBy(x => x.CorrectionVersion).ToListAsync();
            Assert.Equal([1, 2], history.Select(x => x.CorrectionVersion));
            Assert.Equal("MySQL admin correction", history[0].Reason);
            Assert.Equal("MySQL admin correction v2", history[1].Reason);
            var effective = await verify.EmployeeAttendanceDays.AsNoTracking().SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date);
            Assert.Equal(history[1].CorrectedInAtUtc, effective.FirstPunchAtUtc);
            Assert.Equal(history[1].CorrectedOutAtUtc, effective.LastPunchAtUtc);
            var listed = await new AttendanceAdminCorrectionService(verify, fixture.EmployeeTenant, new EffectiveEmploymentResolver(verify, fixture.EmployeeTenant), new NoOpProcessor(), new AttendancePeriodLockService(verify, fixture.EmployeeTenant)).ListAsync(new() { Page = 1, PageSize = 1 });
            Assert.True(listed.Succeeded, listed.Message);
            Assert.Equal(2, listed.Value!.TotalCount);
            Assert.Single(listed.Value.Items);
            Assert.True((await new AttendanceAdminCorrectionService(verify, fixture.EmployeeTenant, new EffectiveEmploymentResolver(verify, fixture.EmployeeTenant), new NoOpProcessor(), new AttendancePeriodLockService(verify, fixture.EmployeeTenant)).GetAsync(history[0].Id)).Succeeded);

            var closedDate = new DateOnly(2026, 10, 6);
            verify.AttendancePeriods.Add(new AttendancePeriod { Id = Guid.NewGuid(), TenantId = fixture.TenantId, Year = 2026, Month = 10, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 31), Status = AttendancePeriodStatus.Closed });
            await verify.SaveChangesAsync();
            var closed = await new AttendanceAdminCorrectionService(verify, fixture.EmployeeTenant, new EffectiveEmploymentResolver(verify, fixture.EmployeeTenant), new NoOpProcessor(), new AttendancePeriodLockService(verify, fixture.EmployeeTenant)).CreateAsync(new(fixture.EmployeeId, closedDate, new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc), null, "closed"));
            Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, closed.Status);
            Assert.Equal(2, await verify.AttendanceAdminCorrections.CountAsync(x => x.EmployeeId == fixture.EmployeeId));
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            await cleanup.AttendanceAdminCorrections.ExecuteDeleteAsync();
            await cleanup.EmployeeAttendanceDays.ExecuteDeleteAsync();
            await cleanup.AttendancePeriods.ExecuteDeleteAsync();
            await cleanup.ShiftApplicabilityRules.ExecuteDeleteAsync();
            await cleanup.Shifts.ExecuteDeleteAsync();
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_admin_correction_http_authorization_and_tenant_isolation_are_enforced()
    {
        var tenantConnection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        var catalogConnection = Environment.GetEnvironmentVariable("HRMS_MYSQL_CATALOG_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(tenantConnection) || string.IsNullOrWhiteSpace(catalogConnection))
            throw SkipException.ForSkip("MySQL Admin Correction HTTP proof requires both connection strings.");

        using var factory = new MySqlApiFactory(tenantConnection, catalogConnection);
        var builder = new AttendanceHttpEmployeeScenarioBuilder(factory);
        var authorized = await builder.CreateAsync("MACA", [Permissions.Attendance.AdminCorrectionManage]);
        var unauthorized = await builder.CreateAsync("MACU");
        await AddApiAttendanceSetupAsync(factory, authorized);
        await AddApiAttendanceSetupAsync(factory, unauthorized);

        var request = new AdminAttendanceCorrectionRequest(authorized.EmployeeId, new(2026, 9, 25), new(2026, 9, 25, 8, 0, 0, DateTimeKind.Utc), new(2026, 9, 25, 16, 0, 0, DateTimeKind.Utc), "MySQL HTTP authorization");
        using var allowed = await authorized.EmployeeClient.PostAsJsonAsync("/api/attendance/admin-corrections", request);
        Assert.True(allowed.IsSuccessStatusCode, await allowed.Content.ReadAsStringAsync());
        await factory.ExecuteInTenantScopeAsync(authorized.Host, async services =>
        {
            var db = services.GetRequiredService<HrmsDbContext>();
            Assert.Single(await db.AttendanceAdminCorrections.AsNoTracking().Where(x => x.EmployeeId == authorized.EmployeeId).ToListAsync());
        });

        var deniedRequest = request with { EmployeeId = unauthorized.EmployeeId, BusinessDate = new(2026, 9, 26) };
        using var denied = await unauthorized.EmployeeClient.PostAsJsonAsync("/api/attendance/admin-corrections", deniedRequest);
        Assert.True(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        await factory.ExecuteInTenantScopeAsync(unauthorized.Host, async services =>
        {
            var db = services.GetRequiredService<HrmsDbContext>();
            Assert.Empty(await db.AttendanceAdminCorrections.AsNoTracking().Where(x => x.EmployeeId == unauthorized.EmployeeId).ToListAsync());
        });

        var tenantB = await builder.CreateAsync("MACB", [Permissions.Attendance.AdminCorrectionManage]);
        await AddApiAttendanceSetupAsync(factory, tenantB);
        var bRequest = request with { EmployeeId = tenantB.EmployeeId, BusinessDate = new(2026, 9, 27) };
        using var bCreated = await tenantB.EmployeeClient.PostAsJsonAsync("/api/attendance/admin-corrections", bRequest);
        Assert.True(bCreated.IsSuccessStatusCode, await bCreated.Content.ReadAsStringAsync());
        var bEnvelope = await bCreated.Content.ReadFromJsonAsync<ApiResponse<AdminAttendanceCorrectionDto>>();
        Assert.NotNull(bEnvelope?.Data);
        using var foreignDetail = await authorized.EmployeeClient.GetAsync($"/api/attendance/admin-corrections/{bEnvelope!.Data!.Id}");
        Assert.True(foreignDetail.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        using var foreignCreate = await authorized.EmployeeClient.PostAsJsonAsync("/api/attendance/admin-corrections", bRequest);
        Assert.True(foreignCreate.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.NotFound);
        var list = await authorized.EmployeeClient.GetFromJsonAsync<ApiResponse<PagedResult<AdminAttendanceCorrectionDto>>>("/api/attendance/admin-corrections", new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.NotNull(list?.Data);
        Assert.DoesNotContain(list!.Data!.Items, x => x.EmployeeId == tenantB.EmployeeId);
    }

    [Fact]
    public async Task MySql_admin_correction_reopen_refreshes_monthly_summary_and_recloses()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("Phase 5D MySQL lifecycle test requires HRMS_MYSQL_TEST_CONNECTION.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            var date = new DateOnly(2026, 10, 12);
            await using var db = fixture.CreateContext(fixture.EmployeeTenant);
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = $"MAL-{fixture.TenantId:N}"[..12], ShiftName = "MySQL lifecycle", IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(17, 0), FullDayWorkMinutes = 480, PlannedDurationMinutes = 480, MinimumWorkMinutes = 480 };
            db.Shifts.Add(shift);
            db.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = fixture.TenantId, RuleName = "MySQL lifecycle employee", EmployeeId = fixture.EmployeeId, ShiftId = shift.Id, Priority = 100, EffectiveFrom = new(2026, 1, 1) });
            var rawIn = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
            var rawOut = new DateTime(2026, 10, 12, 17, 0, 0, DateTimeKind.Utc);
            db.AttendancePunches.AddRange(
                new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = rawIn, CapturedAtUtc = rawIn, Direction = PunchDirection.In, Source = PunchSource.Biometric },
                new AttendancePunch { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = date, PunchAtUtc = rawOut, CapturedAtUtc = rawOut, Direction = PunchDirection.Out, Source = PunchSource.Biometric });
            for (var day = new DateOnly(2026, 10, 1); day <= new DateOnly(2026, 10, 31); day = day.AddDays(1))
                if (day != date) db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = day, Status = EmployeeAttendanceDayStatus.Holiday, ProcessedAtUtc = DateTime.UtcNow });
            var otherEmployeeIds = await db.Employees.AsNoTracking().Where(x => x.TenantId == fixture.TenantId && x.Id != fixture.EmployeeId).Select(x => x.Id).ToListAsync();
            foreach (var employeeId in otherEmployeeIds)
                for (var day = new DateOnly(2026, 10, 1); day <= new DateOnly(2026, 10, 31); day = day.AddDays(1))
                    db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = employeeId, BusinessDate = day, Status = EmployeeAttendanceDayStatus.Holiday, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();

            var processor = new AttendanceDayProcessor(db, fixture.EmployeeTenant, new AttendanceFoundationService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant)), TimeProvider.System, new AttendancePeriodLockService(db, fixture.EmployeeTenant));
            Assert.True((await processor.ProcessAsync(fixture.EmployeeId, date)).Succeeded);
            var monthly = new AttendanceMonthlyProcessor(db, fixture.EmployeeTenant);
            var period = (await monthly.CreatePeriodAsync(new(2026, 10))).Value!;
            Assert.True((await monthly.ProcessAsync(period.Id)).Succeeded);
            var original = await db.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Id && x.EmployeeId == fixture.EmployeeId);
            Assert.Equal(original.SourceDataVersion, await db.AttendancePeriods.Where(x => x.Id == period.Id).Select(x => x.DataVersion).SingleAsync());
            Assert.True((await monthly.CloseAsync(period.Id)).Succeeded);
            var denied = await new AttendanceAdminCorrectionService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant), new NoOpProcessor(), new AttendancePeriodLockService(db, fixture.EmployeeTenant)).CreateAsync(new(fixture.EmployeeId, date, new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc), rawOut, "closed"));
            Assert.Equal(HRMS.Application.Common.ResultStatus.Conflict, denied.Status);
            Assert.True((await monthly.ReopenAsync(period.Id, new("MySQL lifecycle correction"))).Succeeded);
            var service = new AttendanceAdminCorrectionService(db, fixture.EmployeeTenant, new EffectiveEmploymentResolver(db, fixture.EmployeeTenant), processor, new AttendancePeriodLockService(db, fixture.EmployeeTenant));
            var applied = await service.CreateAsync(new(fixture.EmployeeId, date, new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc), new(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc), "MySQL reopened correction"));
            Assert.True(applied.Succeeded, applied.Message);
            var stalePeriod = await db.AttendancePeriods.AsNoTracking().SingleAsync(x => x.Id == period.Id);
            var staleSummary = await db.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Id && x.EmployeeId == fixture.EmployeeId);
            Assert.NotEqual(stalePeriod.DataVersion, staleSummary.SourceDataVersion);
            Assert.True((await new AttendanceMonthlyProcessor(db, fixture.EmployeeTenant).ProcessAsync(period.Id)).Succeeded);
            var refreshed = await db.EmployeeAttendanceMonthlySummaries.AsNoTracking().SingleAsync(x => x.AttendancePeriodId == period.Id && x.EmployeeId == fixture.EmployeeId);
            var correctedDay = await db.EmployeeAttendanceDays.AsNoTracking().SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date);
            Assert.Equal(stalePeriod.DataVersion, refreshed.SourceDataVersion);
            Assert.NotEqual(original.ActualWorkMinutes, refreshed.ActualWorkMinutes);
            Assert.Equal(new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc), correctedDay.FirstPunchAtUtc);
            Assert.Equal(rawIn, await db.AttendancePunches.Where(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date && x.Direction == PunchDirection.In).Select(x => x.PunchAtUtc).SingleAsync());
            Assert.True((await new AttendanceMonthlyProcessor(db, fixture.EmployeeTenant).CloseAsync(period.Id)).Succeeded);
            Assert.Equal(2, await db.AttendancePeriodEvents.CountAsync(x => x.AttendancePeriodId == period.Id && x.EventType == AttendancePeriodEventType.Closed));
            Assert.Equal(1, await db.AttendancePeriodEvents.CountAsync(x => x.AttendancePeriodId == period.Id && x.EventType == AttendancePeriodEventType.Reopened));
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            await cleanup.EmployeeAttendanceMonthlySummaries.ExecuteDeleteAsync();
            await cleanup.AttendancePeriodEvents.ExecuteDeleteAsync();
            await cleanup.AttendancePeriods.ExecuteDeleteAsync();
            await cleanup.AttendanceAdminCorrections.ExecuteDeleteAsync();
            await cleanup.EmployeeAttendanceDays.ExecuteDeleteAsync();
            await cleanup.AttendancePunches.ExecuteDeleteAsync();
            await cleanup.ShiftApplicabilityRules.ExecuteDeleteAsync();
            await cleanup.Shifts.ExecuteDeleteAsync();
            await fixture.CleanupAsync();
        }
    }

    private static async Task AddApiAttendanceSetupAsync(MySqlApiFactory factory, AttendanceHttpEmployeeScenario scenario)
    {
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HrmsDbContext>();
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = scenario.EmployeeId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active, CreatedBy = "mysql-admin-correction-test" });
            var shift = new Shift { Id = Guid.NewGuid(), TenantId = scenario.TenantId, ShiftCode = $"MAC-{scenario.EmployeeId:N}"[..12], ShiftName = "MySQL Admin Correction", IsActive = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(17, 0), FullDayWorkMinutes = 480, PlannedDurationMinutes = 480, MinimumWorkMinutes = 480 };
            db.Shifts.Add(shift);
            db.ShiftApplicabilityRules.Add(new ShiftApplicabilityRule { Id = Guid.NewGuid(), TenantId = scenario.TenantId, RuleName = "MySQL admin correction employee", EmployeeId = scenario.EmployeeId, ShiftId = shift.Id, Priority = 100, EffectiveFrom = new(2026, 1, 1) });
            await db.SaveChangesAsync();
        });
    }

    private sealed class NoOpProcessor : HRMS.Application.Abstractions.IAttendanceDayProcessor
    {
        public Task<HRMS.Application.Common.Result<HRMS.Application.Abstractions.EmployeeAttendanceDayDto>> ProcessAsync(Guid employeeId, DateOnly businessDate, CancellationToken cancellationToken = default)
            => Task.FromResult(HRMS.Application.Common.Result<HRMS.Application.Abstractions.EmployeeAttendanceDayDto>.Failure(HRMS.Application.Common.ResultStatus.ServiceUnavailable, "not used"));
    }
}
