using HRMS.Application.DTOs.Separation;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Xunit.Sdk;

namespace HRMS.Tests;

internal static class SeparationFoundationProviderAcceptance
{
    public static async Task RunAsync(HrmsDbContext db, TestTenantContext tenant, DatabaseProviderType provider)
    {
        var tenantId = Guid.NewGuid(); var userId = Guid.NewGuid(); var employeeId = Guid.NewGuid(); var managerId = Guid.NewGuid(); var managerUserId = Guid.NewGuid(); var hrUserId = Guid.NewGuid();
        tenant.TenantId = tenantId; tenant.UserId = userId;
        db.Tenants.Add(new Tenant { Id = tenantId, TenantCode = $"S{tenantId:N}"[..12], TenantName = $"Separation {provider}", Host = $"{tenantId:N}.test", ShardKey = tenantId.ToString("N") });
        db.Users.Add(new User { Id = userId, TenantId = tenantId, Email = $"{userId:N}@separation.test", PasswordHash = "provider-test-hash", FirstName = "Separation", LastName = "Employee", IsActive = true });
        db.Employees.Add(new Employee { Id = managerId, TenantId = tenantId, EmployeeCode = "SEP-MGR", FirstName = "Separation", LastName = "Manager", Email = $"{managerId:N}@separation.test", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active });
        db.Employees.Add(new Employee { Id = employeeId, TenantId = tenantId, EmployeeCode = "SEP-001", FirstName = "Separation", LastName = "Employee", Email = $"{employeeId:N}@separation.test", DateOfJoining = new(2025, 1, 1), Status = EmployeeStatus.Active, ReportingManagerId = managerId });
        db.Users.Add(new User { Id = managerUserId, TenantId = tenantId, Email = $"{managerUserId:N}@separation.test", PasswordHash = "provider-test-hash", FirstName = "Separation", LastName = "Manager", IsActive = true });
        var linkId = Guid.NewGuid();
        db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = linkId, TenantId = tenantId, SubjectUserId = userId, ActorUserId = userId, Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow, Reason = "Phase 8A provider acceptance", CorrelationId = linkId.ToString("N") });
        db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = employeeId });
        var managerLinkId = Guid.NewGuid();
        db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent { Id = managerLinkId, TenantId = tenantId, SubjectUserId = managerUserId, ActorUserId = managerUserId, Sequence = 1, Operation = "Link", NewLinkId = managerLinkId, AfterEmployeeId = managerId, OccurredAtUtc = DateTime.UtcNow, Reason = "Phase 8B provider acceptance", CorrelationId = managerLinkId.ToString("N") });
        db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = managerLinkId, TenantId = tenantId, UserId = managerUserId, EmployeeId = managerId });
        db.EmployeeEmployments.Add(new EmployeeEmployment { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, FirstHiredDate = new(2025, 1, 1), DateOfJoining = new(2025, 1, 1), NoticePeriod = 30, NoticePeriodUnit = "Days" });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = employeeId, EffectiveFrom = new(2020, 1, 1), ManagerId = managerId, EmploymentStatus = EmployeeStatus.Active });
        db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = managerId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active });
        await db.SaveChangesAsync();

        var service = new SeparationService(db, tenant, new EmployeeIdentityResolver(db, tenant), new EmployeeManagerResolver(db, tenant), TimeProvider.System);
        var reason = await service.CreateReasonAsync(new SeparationReasonRequest("VOLUNTARY", "Voluntary resignation", null, SeparationReasonCategory.Resignation, true, false, true, new(2020, 1, 1), null, 1));
        Assert.True(reason.Succeeded, reason.Message);
        var created = await service.CreateSelfAsync(new SeparationRequest(reason.Value!.Id, new(2026, 9, 23), new(2026, 10, 2), "Provider acceptance"));
        Assert.True(created.Succeeded, created.Message);
        var submitted = await service.SubmitAsync(created.Value!.Id);
        Assert.True(submitted.Succeeded, submitted.Message);
        var duplicate = await service.CreateSelfAsync(new SeparationRequest(reason.Value.Id, new(2026, 9, 23), new(2026, 11, 23), null));
        Assert.False(duplicate.Succeeded); Assert.Equal(ResultStatus.Conflict, duplicate.Status);
        tenant.UserId = managerUserId;
        Assert.True((await new SeparationService(db, tenant, new EmployeeIdentityResolver(db, tenant), new EmployeeManagerResolver(db, tenant), TimeProvider.System).ManagerApproveAsync(created.Value.Id)).Succeeded);
        tenant.UserId = hrUserId;
        var approvalService = new SeparationService(db, tenant, new EmployeeIdentityResolver(db, tenant), new EmployeeManagerResolver(db, tenant), TimeProvider.System);
        var approved = await approvalService.HrApproveAsync(created.Value.Id);
        Assert.True(approved.Succeeded, approved.Message); Assert.Equal(new(2026, 10, 2), approved.Value!.ApprovedLastWorkingDate);
        var employment = await db.EmployeeEmployments.SingleAsync(x => x.EmployeeId == employeeId);
        Assert.Equal(NoticePeriodStatus.Active, employment.NoticeStatus); Assert.Equal(new(2026, 10, 2), employment.NoticeEndDate);
        Assert.Equal(6, await db.EmployeeSeparationEvents.CountAsync(x => x.EmployeeSeparationId == created.Value.Id));
        var notice = await approvalService.GetNoticeAsync(created.Value.Id);
        Assert.True(notice.Succeeded, notice.Message); Assert.Equal(30, notice.Value!.RequiredNoticeDays); Assert.Equal(20, notice.Value.ShortfallDays);
        var waiver = await approvalService.ApplyNoticeWaiverAsync(created.Value.Id, new NoticeWaiverRequest(5, "Provider waiver"));
        Assert.True(waiver.Succeeded, waiver.Message); Assert.Equal(15, waiver.Value!.ShortfallDays);
        var revised = await approvalService.ReviseApprovedLwdAsync(created.Value.Id, new(new(2026, 10, 23), "Provider extension"));
        Assert.True(revised.Succeeded, revised.Message); Assert.Equal(new(2026, 10, 23), revised.Value!.ApprovedLastWorkingDate); Assert.Equal(new(2026, 10, 23), employment.NoticeEndDate);
        Assert.True((await service.GetAsync(created.Value.Id)).Succeeded);
    }
}

public sealed class MySqlSeparationFoundationIntegrationTests
{
    [Fact, Trait("Category", "MySqlIntegration")]
    public async Task MySql_separation_foundation_provider_acceptance_is_repeatable()
    {
        var configured = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) throw SkipException.ForSkip("MySQL Phase 8A tests not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");
        var normalized = MySqlApiFactory.NormalizeConnectionString(configured); var name = $"HRMS_Phase8A_Separation_{Guid.NewGuid():N}";
        var admin = new MySqlConnectionStringBuilder(normalized) { Database = string.Empty }; var connection = new MySqlConnectionStringBuilder(normalized) { Database = name };
        try
        {
            await using (var server = new MySqlConnection(admin.ConnectionString)) { await server.OpenAsync(); await using var create = server.CreateCommand(); create.CommandText = $"CREATE DATABASE `{name}`"; await create.ExecuteNonQueryAsync(); }
            var tenant = new TestTenantContext(); await using var db = new HrmsDbContext(new DbContextOptionsBuilder<HrmsDbContext>().UseMySQL(connection.ConnectionString, x => x.MigrationsAssembly("HRMS.Infrastructure.MySqlMigrations")).Options, tenant);
            await db.Database.MigrateAsync(); await SeparationFoundationProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.MySql); await SeparationFoundationProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.MySql);
        }
        finally { await using var server = new MySqlConnection(admin.ConnectionString); await server.OpenAsync(); await using var drop = server.CreateCommand(); drop.CommandText = $"DROP DATABASE IF EXISTS `{name}`"; await drop.ExecuteNonQueryAsync(); }
    }
}

public sealed class SqlServerSeparationFoundationIntegrationTests : IClassFixture<SqlServerIntegrationTestHarness>
{
    private readonly SqlServerIntegrationTestHarness fixture;
    public SqlServerSeparationFoundationIntegrationTests(SqlServerIntegrationTestHarness fixture) => this.fixture = fixture;

    [SqlServerSeparationFoundationFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_separation_foundation_provider_acceptance()
    {
        var tenant = new TestTenantContext(Guid.NewGuid()); await using var db = fixture.CreateContext(tenant);
        await SeparationFoundationProviderAcceptance.RunAsync(db, tenant, DatabaseProviderType.SqlServer);
    }
}

public sealed class SqlServerSeparationFoundationFactAttribute : FactAttribute
{
    public SqlServerSeparationFoundationFactAttribute() => Skip = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SqlServerIntegrationTestHarness.RequiredEnvironmentVariable))
        ? $"SQL Server Phase 8A tests not executed: {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable} is absent." : null;
}
