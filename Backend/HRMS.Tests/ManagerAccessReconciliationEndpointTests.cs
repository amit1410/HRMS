using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Roles;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Tests;

public sealed class ManagerAccessReconciliationEndpointTests : IClassFixture<HrmsApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HrmsApiFactory _factory;

    public ManagerAccessReconciliationEndpointTests(HrmsApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Reconciliation_then_assignment_verification_is_sequential_and_idempotent()
    {
        var tenantId = SeedData.TenantIds.Demo01;
        var managerEmployeeId = Guid.NewGuid();
        var subordinateEmployeeId = Guid.NewGuid();
        var unrelatedEmployeeId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        var businessDate = DateOnly.FromDateTime(DateTime.UtcNow);

        await _factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo01Host, async services =>
        {
            var db = services.GetRequiredService<IHrmsDbContext>();
            db.Users.Add(new User
            {
                Id = managerUserId, TenantId = tenantId, Email = $"{managerUserId:N}@test.invalid",
                FirstName = "Endpoint", LastName = "Manager", PasswordHash = "test", IsActive = true
            });
            db.Employees.AddRange(
                new Employee
                {
                    Id = managerEmployeeId, TenantId = tenantId, EmployeeCode = $"MGR-{managerEmployeeId:N}",
                    FirstName = "Endpoint", LastName = "Manager", Email = $"{managerUserId:N}@test.invalid",
                    DateOfJoining = new DateOnly(2026, 1, 1), Status = EmployeeStatus.Active
                },
                new Employee
                {
                    Id = subordinateEmployeeId, TenantId = tenantId, EmployeeCode = $"EMP-{subordinateEmployeeId:N}",
                    FirstName = "Endpoint", LastName = "Subordinate", Email = $"{subordinateEmployeeId:N}@test.invalid",
                    DateOfJoining = new DateOnly(2026, 1, 1), Status = EmployeeStatus.Active,
                    ReportingManagerId = managerEmployeeId
                },
                new Employee
                {
                    Id = unrelatedEmployeeId, TenantId = tenantId, EmployeeCode = $"EMP-{unrelatedEmployeeId:N}",
                    FirstName = "Endpoint", LastName = "Unrelated", Email = $"{unrelatedEmployeeId:N}@test.invalid",
                    DateOfJoining = new DateOnly(2026, 1, 1), Status = EmployeeStatus.Active
                });
            var linkId = Guid.NewGuid();
            db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
            {
                Id = linkId, TenantId = tenantId, SubjectUserId = managerUserId, ActorUserId = managerUserId,
                Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = managerEmployeeId,
                OccurredAtUtc = DateTime.UtcNow, Reason = "Endpoint regression setup",
                CorrelationId = $"endpoint-manager-link-{linkId:N}"
            });
            db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink
            {
                LinkId = linkId, TenantId = tenantId, UserId = managerUserId, EmployeeId = managerEmployeeId
            });
            db.EmployeeEmploymentHistory.AddRange(
                new EmployeeEmploymentHistory
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = managerEmployeeId,
                    EffectiveFrom = new DateOnly(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active
                },
                new EmployeeEmploymentHistory
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = subordinateEmployeeId,
                    ManagerId = managerEmployeeId, EffectiveFrom = new DateOnly(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active
                },
                new EmployeeEmploymentHistory
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, EmployeeId = unrelatedEmployeeId,
                    EffectiveFrom = new DateOnly(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active
                });

            // A second existing assignment is required to exercise the production mapping path that
            // previously started multiple EF queries concurrently.
            db.UserRoles.Add(new UserRole
            {
                Id = Guid.NewGuid(), TenantId = tenantId, UserId = managerUserId,
                RoleId = SeedData.RoleId(RoleNames.Employee), EffectiveFrom = new DateOnly(2026, 1, 1),
                AssignmentSource = RoleAssignmentSource.System, AssignmentReason = "Endpoint regression setup"
            });
            await db.SaveChangesAsync();
            Assert.False(await db.UserRoles.AnyAsync(x =>
                x.TenantId == tenantId && x.UserId == managerUserId && x.RoleId == SeedData.RoleId(RoleNames.Manager)));
        });

        using var client = _factory.CreateClientFor(HrmsApiFactory.Demo01Host);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Create(SeedData.Users[0].Id, tenantId, "DEMO01", roles: [RoleNames.TenantAdmin],
                permissions: [Permissions.EmploymentHistory.Change, Permissions.RoleManagement.AssignmentView]));

        var reconcile = await client.PostAsync(
            $"/api/employees/{managerEmployeeId}/employment-history/reconcile-manager-access", null);
        Assert.Equal(HttpStatusCode.OK, reconcile.StatusCode);

        var firstVerification = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<RoleAssignmentDto>>>(
            $"/api/role-assignments/users/{managerUserId}", Json);
        Assert.NotNull(firstVerification?.Data);
        Assert.Equal(2, firstVerification!.Data!.Count);
        var firstManagerAssignment = Assert.Single(firstVerification.Data, x => x.RoleName == RoleNames.Manager);
        Assert.Equal(managerUserId, firstManagerAssignment.UserId);
        Assert.Equal(managerEmployeeId, firstManagerAssignment.EmployeeId);
        Assert.Equal(businessDate, firstManagerAssignment.EffectiveFrom);
        Assert.Null(firstManagerAssignment.EffectiveTo);
        Assert.True(firstManagerAssignment.IsCurrentlyEffective);
        Assert.True(firstManagerAssignment.IsSystemManaged);
        Assert.False(firstManagerAssignment.IsRevoked);

        var secondReconcile = await client.PostAsync(
            $"/api/employees/{managerEmployeeId}/employment-history/reconcile-manager-access", null);
        Assert.Equal(HttpStatusCode.OK, secondReconcile.StatusCode);
        var secondVerification = await client.GetFromJsonAsync<ApiResponse<IReadOnlyList<RoleAssignmentDto>>>(
            $"/api/role-assignments/users/{managerUserId}", Json);
        Assert.NotNull(secondVerification?.Data);
        var secondManagerAssignments = secondVerification.Data!.Where(x => x.RoleName == RoleNames.Manager).ToList();
        var secondManagerAssignment = Assert.Single(secondManagerAssignments);
        Assert.Equal(managerUserId, secondManagerAssignment.UserId);
        Assert.Equal(managerEmployeeId, secondManagerAssignment.EmployeeId);
        Assert.Equal(businessDate, secondManagerAssignment.EffectiveFrom);
        Assert.True(secondManagerAssignment.IsCurrentlyEffective);
        Assert.True(secondManagerAssignment.IsSystemManaged);
        Assert.False(secondManagerAssignment.IsRevoked);

        await _factory.ExecuteInTenantScopeAsync(HrmsApiFactory.Demo01Host, async services =>
        {
            var db = services.GetRequiredService<IHrmsDbContext>();
            var tenant = new TestTenantContext(tenantId, managerUserId);
            var access = new EmployeeAccessScopeService(db, tenant);
            Assert.True(await access.CanAccessEmployeeAsync(subordinateEmployeeId, businessDate));
            Assert.False(await access.CanAccessEmployeeAsync(unrelatedEmployeeId, businessDate));
            Assert.True(await access.CanAccessEmployeeAsync(subordinateEmployeeId, businessDate));
            Assert.Equal(1, await db.UserRoles.CountAsync(x =>
                x.TenantId == tenantId && x.UserId == managerUserId && x.RoleId == SeedData.RoleId(RoleNames.Manager)));
        });
    }
}
