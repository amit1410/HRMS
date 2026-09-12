using System.Net.Http.Headers;
using HRMS.Domain.Entities;
using HRMS.Domain.Authorization;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests.TestSupport;

/// <summary>
/// Test-only setup for the smallest Attendance HTTP identity graph. It creates data through the real
/// catalog/shard services, while actions under test still travel through the real HTTP pipeline.
/// </summary>
public sealed class AttendanceHttpEmployeeScenarioBuilder(HrmsApiFactory factory)
{
    public async Task<AttendanceHttpEmployeeScenario> CreateAsync(
        string? prefix = null,
        IEnumerable<string>? permissions = null)
    {
        var shard = await factory.CreateTestTenantAsync(prefix);
        var identity = await CreateLinkedIdentityAsync(shard, "Linked", "Employee", permissions);
        var client = CreateClient($"http://{shard.Host}", identity.UserId, shard.TenantId, shard.TenantCode, identity.Email, permissions);
        return new AttendanceHttpEmployeeScenario(
            shard.TenantId, shard.TenantCode, $"http://{shard.Host}", identity.UserId, identity.EmployeeId, identity.EmployeeCode, client);
    }

    public async Task<AttendanceHttpManagerScenario> CreateManagerAsync(
        string? prefix = null,
        DateOnly? relationshipEffectiveFrom = null,
        DateOnly? relationshipEffectiveTo = null,
        IEnumerable<string>? employeePermissions = null,
        IEnumerable<string>? managerPermissions = null)
    {
        var employee = await CreateAsync(prefix, employeePermissions);
        var manager = await CreateLinkedIdentityAsync(
            new HRMS.Application.Abstractions.ShardDescriptor(
                employee.TenantId, employee.TenantCode, new Uri(employee.Host).Host,
                $"unused-{employee.TenantId:N}", HRMS.Domain.Enums.TenantStatus.Active,
                HRMS.Domain.Enums.DatabaseProviderType.SqlServer),
            "Attendance", "Manager", permissions: managerPermissions ?? [Permissions.Attendance.RegularizationApprove, Permissions.Attendance.OnDutyApprove]);
        var effectiveFrom = relationshipEffectiveFrom ?? new DateOnly(2026, 9, 10);

        await ExecuteTenantAsync(employee, async db =>
        {
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
            {
                Id = Guid.NewGuid(), TenantId = employee.TenantId, EmployeeId = employee.EmployeeId,
                EffectiveFrom = effectiveFrom, EffectiveTo = relationshipEffectiveTo,
                ManagerId = manager.EmployeeId, EmploymentStatus = EmployeeStatus.Active,
                CreatedBy = "attendance-http-test"
            });
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
            {
                Id = Guid.NewGuid(), TenantId = employee.TenantId, EmployeeId = manager.EmployeeId,
                EffectiveFrom = effectiveFrom, EffectiveTo = relationshipEffectiveTo,
                EmploymentStatus = EmployeeStatus.Active, CreatedBy = "attendance-http-test"
            });
            await db.SaveChangesAsync();
        });

        return new AttendanceHttpManagerScenario(
            employee.TenantId, employee.TenantCode, employee.Host,
            employee.UserId, employee.EmployeeId, manager.UserId, manager.EmployeeId,
            employee.EmployeeClient, manager.Client, effectiveFrom, relationshipEffectiveTo);
    }

    public async Task<AttendanceHttpEmployeeScenario> AddLinkedEmployeeAsync(
        AttendanceHttpManagerScenario scenario,
        string? firstName = null,
        IEnumerable<string>? permissions = null)
    {
        var shard = new HRMS.Application.Abstractions.ShardDescriptor(
            scenario.TenantId, scenario.TenantCode, new Uri(scenario.Host).Host,
            $"unused-{scenario.TenantId:N}", HRMS.Domain.Enums.TenantStatus.Active,
            HRMS.Domain.Enums.DatabaseProviderType.SqlServer);
        var identity = await CreateLinkedIdentityAsync(shard, firstName ?? "Second", "Employee", permissions);
        await ExecuteTenantAsync(scenario, async db =>
        {
            db.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
            {
                Id = Guid.NewGuid(), TenantId = scenario.TenantId, EmployeeId = identity.EmployeeId,
                EffectiveFrom = scenario.RelationshipEffectiveFrom, ManagerId = scenario.ManagerEmployeeId,
                EmploymentStatus = EmployeeStatus.Active, CreatedBy = "attendance-http-test"
            });
            await db.SaveChangesAsync();
        });
        return new AttendanceHttpEmployeeScenario(
            scenario.TenantId, scenario.TenantCode, scenario.Host, identity.UserId,
            identity.EmployeeId, identity.EmployeeCode, identity.Client);
    }

    public async Task<AttendanceHttpUnlinkedUser> CreateUnlinkedUserAsync(
        AttendanceHttpEmployeeScenario scenario)
    {
        var userId = Guid.NewGuid();
        var email = $"unlinked-{userId:N}@test.invalid";
        await factory.ExecuteInTenantScopeAsync(scenario.Host, async services =>
        {
            var db = services.GetRequiredService<HrmsDbContext>();
            db.Users.Add(new User
            {
                Id = userId,
                TenantId = scenario.TenantId,
                Email = email,
                PasswordHash = "test-only-not-used-by-token-auth",
                FirstName = "Unlinked",
                LastName = "User",
                IsActive = true
            });
            await db.SaveChangesAsync();
        });

        return new AttendanceHttpUnlinkedUser(
            userId, CreateClient(scenario.Host, userId, scenario.TenantId, scenario.TenantCode, email));
    }

    /// <summary>Runs verification against a fresh tenant-selected scope and DbContext.</summary>
    public Task ExecuteTenantAsync(
        AttendanceHttpEmployeeScenario scenario,
        Func<HrmsDbContext, Task> action) =>
        ExecuteTenantServicesAsync(scenario, services =>
            action(services.GetRequiredService<HrmsDbContext>()));

    public Task ExecuteTenantServicesAsync(
        AttendanceHttpEmployeeScenario scenario,
        Func<IServiceProvider, Task> action) =>
        ExecuteTenantServicesAsync(scenario.Host, action);

    public Task ExecuteTenantServicesAsync(
        AttendanceHttpManagerScenario scenario,
        Func<IServiceProvider, Task> action) =>
        ExecuteTenantServicesAsync(scenario.Host, action);

    public Task ExecuteTenantAsync(
        AttendanceHttpManagerScenario scenario,
        Func<HrmsDbContext, Task> action) =>
        ExecuteTenantServicesAsync(scenario, services =>
            action(services.GetRequiredService<HrmsDbContext>()));

    public Task ExecuteTenantAsync(
        AttendanceHttpWorkflowScenario scenario,
        Func<HrmsDbContext, Task> action) =>
        ExecuteTenantServicesAsync(scenario.Host, services =>
            action(services.GetRequiredService<HrmsDbContext>()));

    private Task ExecuteTenantServicesAsync(string host, Func<IServiceProvider, Task> action) =>
        factory.ExecuteInTenantScopeAsync(host, action);

    private HttpClient CreateClient(
        string host,
        Guid userId,
        Guid tenantId,
        string tenantCode,
        string email,
        IEnumerable<string>? permissions = null)
    {
        var client = factory.CreateClientFor(host);
        var token = TestTokens.Create(userId, tenantId, tenantCode, email, permissions: permissions);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<LinkedIdentity> CreateLinkedIdentityAsync(
        HRMS.Application.Abstractions.ShardDescriptor shard,
        string firstName,
        string lastName,
        IEnumerable<string>? permissions = null)
    {
        var userId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var employeeCode = $"EMP-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
        var email = $"employee-{userId:N}@test.invalid";
        var linkId = Guid.NewGuid();

        await factory.ExecuteInTenantScopeAsync($"http://{shard.Host}", async services =>
        {
            var db = services.GetRequiredService<HrmsDbContext>();
            var now = DateTime.UtcNow;
            db.Employees.Add(new Employee
            {
                Id = employeeId, TenantId = shard.TenantId, EmployeeCode = employeeCode,
                FirstName = firstName, LastName = lastName, Email = email,
                DateOfJoining = DateOnly.FromDateTime(now.AddYears(-1))
            });
            db.Users.Add(new User
            {
                Id = userId, TenantId = shard.TenantId, Email = email,
                PasswordHash = "test-only-not-used-by-token-auth", FirstName = firstName,
                LastName = lastName, IsActive = true
            });
            db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
            {
                Id = linkId, TenantId = shard.TenantId, SubjectUserId = userId, ActorUserId = userId,
                Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId,
                OccurredAtUtc = now, Reason = "Test setup", CorrelationId = Guid.NewGuid().ToString("N")
            });
            db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink
            {
                LinkId = linkId, TenantId = shard.TenantId, UserId = userId, EmployeeId = employeeId
            });
            await db.SaveChangesAsync();
        });

        return new LinkedIdentity(userId, employeeId, employeeCode, email,
            CreateClient($"http://{shard.Host}", userId, shard.TenantId, shard.TenantCode, email, permissions));
    }

    private sealed record LinkedIdentity(Guid UserId, Guid EmployeeId, string EmployeeCode, string Email, HttpClient Client);
}

public sealed record AttendanceHttpEmployeeScenario(
    Guid TenantId,
    string TenantCode,
    string Host,
    Guid UserId,
    Guid EmployeeId,
    string EmployeeCode,
    HttpClient EmployeeClient);

public sealed record AttendanceHttpUnlinkedUser(Guid UserId, HttpClient Client);

public sealed record AttendanceHttpManagerScenario(
    Guid TenantId,
    string TenantCode,
    string Host,
    Guid EmployeeUserId,
    Guid EmployeeId,
    Guid ManagerUserId,
    Guid ManagerEmployeeId,
    HttpClient EmployeeClient,
    HttpClient ManagerClient,
    DateOnly RelationshipEffectiveFrom,
    DateOnly? RelationshipEffectiveTo);
