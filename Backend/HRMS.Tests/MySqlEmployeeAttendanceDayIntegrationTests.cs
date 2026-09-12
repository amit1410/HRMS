using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlEmployeeAttendanceDayIntegrationTests
{
    [Fact]
    public async Task Real_mysql_processor_persists_present_day()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(); var shift = Shift(f.TenantId, "DAY"); db.Shifts.Add(shift); await db.SaveChangesAsync();
            db.AttendancePunches.AddRange(Punch(f, new DateTime(2026, 10, 10, 9, 0, 0), PunchDirection.In), Punch(f, new DateTime(2026, 10, 10, 18, 0, 0), PunchDirection.Out)); await db.SaveChangesAsync();
            var result = await Processor(db, f).ProcessAsync(f.EmployeeId, new(2026, 10, 10));
            Assert.True(result.Succeeded, result.Message); Assert.Equal(EmployeeAttendanceDayStatus.Present, result.Value!.Status); Assert.Equal(1, result.Value.SessionCount);
        });
    }

    [Fact]
    public async Task Real_mysql_processor_reprocesses_same_daily_row()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(); var shift = Shift(f.TenantId, "REPROCESS"); db.Shifts.Add(shift); db.AttendancePunches.Add(Punch(f, new DateTime(2026, 10, 10, 9, 0, 0), PunchDirection.In)); await db.SaveChangesAsync();
            var processor = Processor(db, f); var first = await processor.ProcessAsync(f.EmployeeId, new(2026, 10, 10)); db.AttendancePunches.Add(Punch(f, new DateTime(2026, 10, 10, 18, 0, 0), PunchDirection.Out)); await db.SaveChangesAsync(); var second = await processor.ProcessAsync(f.EmployeeId, new(2026, 10, 10));
            Assert.Equal(EmployeeAttendanceDayStatus.Incomplete, first.Value!.Status); Assert.Equal(EmployeeAttendanceDayStatus.Present, second.Value!.Status); Assert.Equal(first.Value.Id, second.Value.Id);
        });
    }

    [Fact]
    public async Task Real_mysql_processor_handles_calendar_non_working_day()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(); db.Holidays.Add(new Holiday { Id = Guid.NewGuid(), TenantId = f.TenantId, Name = "Phase2 Holiday", Date = new(2026, 10, 10), IsActive = true }); await db.SaveChangesAsync();
            var result = await Processor(db, f).ProcessAsync(f.EmployeeId, new(2026, 10, 10)); Assert.True(result.Succeeded, result.Message); Assert.Equal(EmployeeAttendanceDayStatus.Holiday, result.Value!.Status);
        });
    }

    [Fact]
    public async Task Real_mysql_processor_is_tenant_scoped()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(new TestTenantContext(f.OtherTenantId)); var result = await Processor(db, f, f.OtherTenantId).ProcessAsync(f.EmployeeId, new(2026, 10, 10)); Assert.False(result.Succeeded); Assert.Equal(HRMS.Application.Common.ResultStatus.NotFound, result.Status);
        });
    }

    private static AttendanceDayProcessor Processor(HRMS.Infrastructure.Persistence.HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture f, Guid? tenantId = null)
    { var tenant = tenantId.HasValue ? new TestTenantContext(tenantId.Value) : f.EmployeeTenant; var employment = new EffectiveEmploymentResolver(db, tenant); var roster = new AttendanceFoundationService(db, tenant, employment, new WorkingDayCalendarResolver(db, tenant, employment)); return new(db, tenant, roster, TimeProvider.System); }
    private static Shift Shift(Guid tenantId, string code) => new() { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = $"{code}-{Guid.NewGuid():N}"[..Math.Min(25, code.Length + 9)], ShiftName = code, IsDefault = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1 };
    private static AttendancePunch Punch(MySqlLeaveLifecycleIntegrationTests.Fixture f, DateTime dateTime, PunchDirection direction) => new() { Id = Guid.NewGuid(), TenantId = f.TenantId, EmployeeId = f.EmployeeId, PunchAtUtc = DateTime.SpecifyKind(dateTime, DateTimeKind.Utc), BusinessDate = new(dateTime.Year, dateTime.Month, dateTime.Day), Direction = direction, Source = PunchSource.Biometric, ExternalPunchId = $"day-{Guid.NewGuid():N}", CapturedAtUtc = DateTime.UtcNow };
    private static async Task WithFixture(Func<MySqlLeaveLifecycleIntegrationTests.Fixture, Task> action)
    { var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION"); if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Phase 2 day tests not executed: connection is absent."); var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection); try { await fixture.SeedAsync(); await action(fixture); } finally { await fixture.CleanupAttendanceAsync(); await fixture.CleanupAsync(); } }
}
