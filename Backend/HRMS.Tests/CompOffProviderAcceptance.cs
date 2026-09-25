using HRMS.Application.Services;
using HRMS.Application.Abstractions;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

internal static class CompOffProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant)
    {
        var tenantId = tenant.TenantId ?? Guid.NewGuid();
        tenant.TenantId = tenantId;
        if (!await db.Tenants.AnyAsync(x => x.Id == tenantId)) db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"P6D{tenantId:N}"[..10], TenantName = "Phase 6D Provider", Host = $"p6d-{tenantId:N}.test", ShardKey = tenantId.ToString("N"), Status = TenantStatus.Active });
        var employeeId = Guid.NewGuid(); var dayId = Guid.NewGuid();
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "P6D-001", FirstName = "Provider", LastName = "CompOff", Email = $"{employeeId:N}@test.local", DateOfJoining = new(2026, 1, 1), Status = EmployeeStatus.Active });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
        db.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay { Id = dayId, TenantId = tenantId, EmployeeId = employeeId, BusinessDate = new(2026, 9, 15), RosterDayType = RosterDayType.WeeklyOff, Status = EmployeeAttendanceDayStatus.WeeklyOff, ExpectedWorkMinutes = 480, WorkedMinutes = 480, ProcessedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = new CompOffService(db, tenant, TimeProvider.System);
        var policy = await service.CreatePolicyAsync(new("P6D", "Provider Comp-Off", new(2026, 1, 1), null, true, true, true, 240, 1, CompOffRoundingMode.None, 0, null, null, 10, null, false, true, 1, CompOffBenefitMode.CompOffOnly, "All"));
        Assert.True(policy.Succeeded, policy.Message);
        var request = new CompOffEarnRequest(employeeId, new(2026, 9, 15), CompOffSourceType.WeekOff, dayId, 1);
        var earning = await service.EarnAsync(request);
        Assert.True(earning.Succeeded, earning.Message);
        var duplicate = await service.EarnAsync(request);
        Assert.True(duplicate.Succeeded);
        Assert.Equal(1, await db.CompOffEarnings.CountAsync(x => x.TenantId == tenantId && x.SourceAttendanceDayId == dayId));
        Assert.Equal(1, await db.CompOffLedgerEntries.CountAsync(x => x.TenantId == tenantId && x.EntryType == CompOffLedgerEntryType.Credit));
        Assert.Equal(480, (await service.GetBalanceAsync(employeeId)).Value!.AvailableMinutes);
        Assert.Equal(1, (await service.ExpireAsync(new(2026, 9, 26))).Value);
        Assert.Equal(0, (await service.GetBalanceAsync(employeeId)).Value!.AvailableMinutes);
    }
}

public sealed class MySqlCompOffIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_comp_off_provider_acceptance()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("Phase 6D MySQL operator gate pending: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var databaseName = $"HRMS_Phase6D_CompOff_{Guid.NewGuid():N}";
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured);
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty };
        var connection = new MySqlConnectionStringBuilder(normalized) { Database = databaseName };
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var command = server.CreateCommand(); command.CommandText = $"CREATE DATABASE `{databaseName}`"; await command.ExecuteNonQueryAsync(); }
            var tenant = new TestTenantContext(Guid.NewGuid());
            await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
            await db.Database.MigrateAsync(); await CompOffProviderAcceptance.RunAsync(db, tenant);
        }
        finally
        {
            await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{databaseName}`"; await drop.ExecuteNonQueryAsync();
        }
    }
}

public sealed class SqlServerCompOffIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerCompOffIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [Fact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_comp_off_provider_acceptance()
    {
        if (!fixture.IsConfigured) throw SkipException.ForSkip("Phase 6D SQL Server operator gate pending: HRMS_SQLSERVER_TEST_CONNECTION is absent.");
        var tenant = new TestTenantContext(Guid.NewGuid());
        await using var db = fixture.CreateContext(tenant);
        await db.Database.MigrateAsync(); await CompOffProviderAcceptance.RunAsync(db, tenant);
    }
}
