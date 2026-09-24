using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public static class AttendanceMonthlyFinalizationProviderAcceptance
{
    public static async Task RunAsync(HRMS.Infrastructure.Persistence.HrmsDbContext db, TestTenantContext tenant)
    {
        var processor = new AttendanceMonthlyProcessor(db, tenant);
        var created = await processor.CreatePeriodAsync(new(2026, 12));
        Assert.True(created.Succeeded, created.Message);
        var processed = await processor.ProcessAsync(created.Value!.Id);
        Assert.True(processed.Succeeded, processed.Message);
        var preview = await processor.GetClosePreviewAsync(created.Value.Id);
        Assert.True(preview.Succeeded && preview.Value!.CanClose, preview.Message);
        var closed = await processor.CloseAsync(created.Value.Id);
        Assert.True(closed.Succeeded, closed.Message);
        var employeeCount = await db.EmployeeAttendanceMonthlySummaries.CountAsync(x => x.AttendancePeriodId == created.Value.Id);
        Assert.Equal(employeeCount, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == created.Value.Id && x.IsCurrent));
        Assert.True((await processor.ReopenAsync(created.Value.Id, new("provider acceptance correction"))).Succeeded);
        Assert.True((await processor.ProcessAsync(created.Value.Id)).Succeeded);
        Assert.True((await processor.CloseAsync(created.Value.Id)).Succeeded);
        Assert.Equal(employeeCount * 2, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == created.Value.Id));
        Assert.Equal(employeeCount, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == created.Value.Id && x.IsCurrent));
        Assert.Equal(employeeCount, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == created.Value.Id && x.Version == 1));
        Assert.Equal(employeeCount, await db.PayrollAttendanceSnapshots.CountAsync(x => x.AttendancePeriodId == created.Value.Id && x.Version == 2));
    }
}

[Collection("Attendance MySQL")]
public sealed class MySqlAttendanceMonthlyFinalizationIntegrationTests
{
    [Phase6BMySqlFact]
    public async Task MySql_attendance_monthly_finalization_provider_acceptance()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) throw SkipException.ForSkip("Phase 6B MySQL provider gate pending: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var fixture = new MySqlLeaveLifecycleIntegrationTests.Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var db = fixture.CreateContext(fixture.EmployeeTenant);
            for (var day = new DateOnly(2026, 12, 1); day <= new DateOnly(2026, 12, 31); day = day.AddDays(1)) db.EmployeeAttendanceDays.AddRange(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId, BusinessDate = day, Status = EmployeeAttendanceDayStatus.Present, ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow }, new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.ManagerId, BusinessDate = day, Status = EmployeeAttendanceDayStatus.Present, ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await AttendanceMonthlyFinalizationProviderAcceptance.RunAsync(db, fixture.EmployeeTenant);
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext(fixture.TenantId));
            await cleanup.PayrollAttendanceSnapshots.ExecuteDeleteAsync();
            await cleanup.EmployeeAttendanceDays.ExecuteDeleteAsync();
            await cleanup.EmployeeAttendanceMonthlySummaries.ExecuteDeleteAsync();
            await cleanup.AttendancePeriodEvents.ExecuteDeleteAsync();
            await cleanup.AttendancePeriods.ExecuteDeleteAsync();
            await fixture.CleanupAsync();
        }
    }
}

public sealed class SqlServerAttendanceMonthlyFinalizationIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerAttendanceMonthlyFinalizationIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [Phase6BSqlServerFact]
    public async Task SqlServer_attendance_monthly_finalization_provider_acceptance()
    {
        if (!fixture.IsConfigured) throw SkipException.ForSkip("Phase 6B SQL Server operator gate pending: HRMS_SQLSERVER_TEST_CONNECTION is absent.");
        var tenantId = Guid.NewGuid();
        try
        {
            await using (var setup = fixture.CreateContext(new TestTenantContext()))
            {
                setup.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"P6B{tenantId:N}"[..10], TenantName = "Phase 6B", Host = $"p6b-{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.SqlServer });
                await setup.SaveChangesAsync();
            }
            await using var db = fixture.CreateContext(new TestTenantContext(tenantId));
            var employeeId = Guid.NewGuid();
            db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "P6B-001", FirstName = "Phase", LastName = "SixB", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2020, 1, 1) });
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            for (var day = new DateOnly(2026, 12, 1); day <= new DateOnly(2026, 12, 31); day = day.AddDays(1)) db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, BusinessDate = day, Status = EmployeeAttendanceDayStatus.Present, ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();
            await AttendanceMonthlyFinalizationProviderAcceptance.RunAsync(db, new TestTenantContext(tenantId));
        }
        finally
        {
            await using var cleanup = fixture.CreateContext(new TestTenantContext());
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PayrollAttendanceSnapshots] WHERE [TenantId] = {tenantId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EmployeeAttendanceMonthlySummaries] WHERE [TenantId] = {tenantId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [AttendancePeriodEvents] WHERE [TenantId] = {tenantId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [AttendancePeriods] WHERE [TenantId] = {tenantId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EmployeeAttendanceDays] WHERE [TenantId] = {tenantId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EmployeeEmploymentHistory] WHERE [TenantId] = {tenantId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Employees] WHERE [TenantId] = {tenantId}");
            await cleanup.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Tenants] WHERE [Id] = {tenantId}");
        }
    }
}

public sealed class Phase6BMySqlFactAttribute : FactAttribute
{
    public Phase6BMySqlFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION")) ? "Phase 6B MySQL operator gate pending: HRMS_MYSQL_TEST_CONNECTION is absent." : null;
}

public sealed class Phase6BSqlServerFactAttribute : FactAttribute
{
    public Phase6BSqlServerFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HRMS_SQLSERVER_TEST_CONNECTION")) ? "Phase 6B SQL Server operator gate pending: HRMS_SQLSERVER_TEST_CONNECTION is absent." : null;
}
