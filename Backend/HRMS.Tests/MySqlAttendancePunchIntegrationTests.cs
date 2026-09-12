using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class MySqlAttendancePunchIntegrationTests
{
    [Fact]
    public async Task Real_mysql_punch_persists_source_and_business_date()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext();
            var shift = Shift(f.TenantId, "PERSIST"); db.Shifts.Add(shift); await db.SaveChangesAsync();
            var service = Ingestion(db, f);
            var result = await service.IngestAsync(new(f.EmployeeId, Utc(2026, 10, 10, 9), PunchDirection.In, PunchSource.Biometric, $"mysql-{Guid.NewGuid():N}"));
            Assert.True(result.Succeeded, result.Message);
            var saved = await db.AttendancePunches.SingleAsync(x => x.Id == result.Value!.Id);
            Assert.Equal(PunchSource.Biometric, saved.Source); Assert.Equal(new DateOnly(2026, 10, 10), saved.BusinessDate);
        });
    }

    [Fact]
    public async Task Real_mysql_duplicate_external_punch_is_idempotent()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(); var shift = Shift(f.TenantId, "IDEMP"); db.Shifts.Add(shift); await db.SaveChangesAsync();
            var service = Ingestion(db, f); var key = $"mysql-{Guid.NewGuid():N}"; var request = new HRMS.Application.Abstractions.AttendancePunchIngestionRequest(f.EmployeeId, Utc(2026, 10, 10, 9), PunchDirection.In, PunchSource.Biometric, key);
            var first = await service.IngestAsync(request); var second = await service.IngestAsync(request);
            Assert.True(first.Succeeded, first.Message); Assert.True(second.Succeeded, second.Message); Assert.Equal(first.Value!.Id, second.Value!.Id);
            Assert.Equal(1, await db.AttendancePunches.CountAsync(x => x.ExternalPunchId == key));
        });
    }

    [Fact]
    public async Task Real_mysql_same_external_id_is_scoped_by_tenant()
    {
        await WithFixture(async f =>
        {
            await using var a = f.CreateContext(); var shiftA = Shift(f.TenantId, "TENANT-A"); a.Shifts.Add(shiftA);
            await using var b = f.CreateContext(new TestTenantContext(f.OtherTenantId)); var employee = new Employee { Id = Guid.NewGuid(), TenantId = f.OtherTenantId, EmployeeCode = $"P{Guid.NewGuid():N}"[..10], FirstName = "Other", LastName = "Tenant", DateOfJoining = new(2026, 1, 1) }; var shiftB = Shift(f.OtherTenantId, "TENANT-B"); b.Employees.Add(employee); b.Shifts.Add(shiftB); b.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = f.OtherTenantId, EmployeeId = employee.Id, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active }); await b.SaveChangesAsync(); await a.SaveChangesAsync();
            var key = $"shared-{Guid.NewGuid():N}"; var ra = await Ingestion(a, f).IngestAsync(new(f.EmployeeId, Utc(2026, 10, 10, 9), PunchDirection.In, PunchSource.Biometric, key)); var rb = await Ingestion(b, f, f.OtherTenantId).IngestAsync(new(employee.Id, Utc(2026, 10, 10, 9), PunchDirection.In, PunchSource.Biometric, key));
            Assert.True(ra.Succeeded, ra.Message); Assert.True(rb.Succeeded, rb.Message); Assert.NotEqual(ra.Value!.Id, rb.Value!.Id);
        });
    }

    [Fact]
    public async Task Real_mysql_overnight_punch_uses_shift_business_date()
    {
        await WithFixture(async f =>
        {
            await using var db = f.CreateContext(); var shift = Shift(f.TenantId, "NIGHT"); shift.StartTime = new(22, 0); shift.EndTime = new(6, 0); shift.CrossesMidnight = true; shift.MaximumPostShiftMinutes = 15; db.Shifts.Add(shift); await db.SaveChangesAsync();
            var service = Ingestion(db, f); var date = new DateOnly(2026, 10, 10); var first = await service.IngestAsync(new(f.EmployeeId, Utc(2026, 10, 10, 22), PunchDirection.In, PunchSource.Biometric, $"night-in-{Guid.NewGuid():N}")); var last = await service.IngestAsync(new(f.EmployeeId, Utc(2026, 10, 11, 6, 5), PunchDirection.Out, PunchSource.Biometric, $"night-out-{Guid.NewGuid():N}"));
            Assert.True(first.Succeeded, first.Message); Assert.True(last.Succeeded, last.Message); Assert.Equal(date, last.Value!.BusinessDate);
        });
    }

    private static AttendancePunchIngestionService Ingestion(HRMS.Infrastructure.Persistence.HrmsDbContext db, MySqlLeaveLifecycleIntegrationTests.Fixture f, Guid? tenantId = null)
    { var tenant = tenantId.HasValue ? new TestTenantContext(tenantId.Value) : f.EmployeeTenant; var employment = new EffectiveEmploymentResolver(db, tenant); var roster = new AttendanceFoundationService(db, tenant, employment, new WorkingDayCalendarResolver(db, tenant, employment)); return new(db, tenant, roster, new AttendanceBusinessDateResolver(), new AttendanceBusinessTimeZoneProvider(), TimeProvider.System); }
    private static Shift Shift(Guid tenantId, string code) => new() { Id = Guid.NewGuid(), TenantId = tenantId, ShiftCode = $"{code}-{Guid.NewGuid():N}"[..Math.Min(25, code.Length + 9)], ShiftName = code, IsDefault = true, EffectiveFrom = new(2026, 1, 1), StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540, FullDayWorkMinutes = 480, MinimumWorkMinutes = 1, AllowedAttendanceSources = AttendanceSource.Biometric };
    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    private static async Task WithFixture(Func<MySqlLeaveLifecycleIntegrationTests.Fixture, Task> action)
    { var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION"); if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("MySQL Phase 2 punch tests not executed: connection is absent."); var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection); try { await fixture.SeedAsync(); await action(fixture); } finally { await fixture.CleanupAttendanceAsync(); await using var cleanup = fixture.CreateContext(new TestTenantContext()); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `EmployeeEmploymentHistory` WHERE `TenantId` = {fixture.OtherTenantId}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Employees` WHERE `TenantId` = {fixture.OtherTenantId}"); await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Shifts` WHERE `TenantId` = {fixture.OtherTenantId}"); await fixture.CleanupAsync(); } }
}
