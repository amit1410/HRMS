using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Domain.Authorization;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Persistence.Seed;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

public sealed class SqlServerAttendanceOperationsIntegrationTests
{
    [Fact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_phase6e_attendance_operations_provider_acceptance()
    {
        var harness = new SqlServerIntegrationTestHarness();
        if (!harness.IsConfigured)
            throw SkipException.ForSkip($"SQL Server Phase 6E acceptance requires {SqlServerIntegrationTestHarness.RequiredEnvironmentVariable}.");

        var fixture = new SqlServerFixture();
        await using var disposable = await harness.CreateDisposableDatabaseAsync("HRMS_Phase6E_AttendanceOps_Test_");
        await using var db = disposable.CreateContext(fixture.ManagerTenant);
        await fixture.SeedAsync(db);
        await AttendanceOperationsProviderAcceptance.RunAsync(db, fixture);
    }

    private sealed class SqlServerFixture : IAttendanceOperationsProviderFixture
    {
        public string ProviderName => "SQL Server";
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid OtherTenantId { get; } = Guid.NewGuid();
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid EmployeeUserId { get; } = Guid.NewGuid();
        public Guid ManagerUserId { get; } = Guid.NewGuid();
        public Guid LeaveTypeId { get; } = Guid.NewGuid();
        public TestTenantContext EmployeeTenant { get; }
        public TestTenantContext ManagerTenant { get; }

        public SqlServerFixture()
        {
            EmployeeTenant = new(TenantId, EmployeeUserId);
            ManagerTenant = new(TenantId, ManagerUserId);
        }

        public HRMS.Application.Abstractions.ILeaveRequestSubmissionLock CreateLeaveSubmissionLock(HrmsDbContext db) =>
            new SqlServerLeaveRequestSubmissionLock(db);

        public async Task SeedAsync(HrmsDbContext db)
        {
            db.Tenants.AddRange(
                new Tenant { Id = TenantId, TenantCode = $"S{TenantId:N}"[..12], TenantName = "Phase 6E SQL test", Host = $"s-{TenantId:N}.test", ShardKey = TenantId.ToString("N"), Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.SqlServer },
                new Tenant { Id = OtherTenantId, TenantCode = $"O{OtherTenantId:N}"[..12], TenantName = "Phase 6E other tenant", Host = $"o-{OtherTenantId:N}.test", ShardKey = OtherTenantId.ToString("N"), Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.SqlServer });
            db.Users.AddRange(
                new User { Id = EmployeeUserId, TenantId = TenantId, Email = $"{EmployeeUserId:N}@test.invalid", PasswordHash = "provider-test-hash", IsActive = true },
                new User { Id = ManagerUserId, TenantId = TenantId, Email = $"{ManagerUserId:N}@test.invalid", PasswordHash = "provider-test-hash", IsActive = true });
            db.Employees.AddRange(
                new Employee { Id = EmployeeId, TenantId = TenantId, EmployeeCode = $"E{EmployeeId:N}"[..12], FirstName = "Provider", LastName = "Employee", Email = $"e-{EmployeeId:N}@test.invalid", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active, ReportingManagerId = ManagerId },
                new Employee { Id = ManagerId, TenantId = TenantId, EmployeeCode = $"M{ManagerId:N}"[..12], FirstName = "Provider", LastName = "Manager", Email = $"m-{ManagerId:N}@test.invalid", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active });
            db.EmployeeEmploymentHistory.AddRange(
                new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, EffectiveFrom = new(2020, 1, 1), ManagerId = ManagerId, EmploymentStatus = EmployeeStatus.Active },
                new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = ManagerId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            var employeeLink = Guid.NewGuid();
            var managerLink = Guid.NewGuid();
            db.AccountEmployeeCurrentLinks.AddRange(
                new AccountEmployeeCurrentLink { LinkId = employeeLink, TenantId = TenantId, UserId = EmployeeUserId, EmployeeId = EmployeeId },
                new AccountEmployeeCurrentLink { LinkId = managerLink, TenantId = TenantId, UserId = ManagerUserId, EmployeeId = ManagerId });
            db.AccountEmployeeLinkEvents.AddRange(
                LinkEvent(employeeLink, EmployeeUserId, EmployeeId),
                LinkEvent(managerLink, ManagerUserId, ManagerId));
            var employeeRole = SeedData.RoleId(RoleNames.Employee);
            if (!await db.Roles.AnyAsync(x => x.Id == employeeRole))
                db.Roles.Add(new Role { Id = employeeRole, Name = RoleNames.Employee, Description = "Standard employee." });
            foreach (var permissionName in SeedData.RolePermissionMap[RoleNames.Employee])
            {
                var permissionId = SeedData.PermissionId(permissionName);
                if (!await db.Permissions.AnyAsync(x => x.Id == permissionId))
                    db.Permissions.Add(new Permission { Id = permissionId, Name = permissionName, Description = permissionName.Replace('.', ' ') });
                if (!await db.RolePermissions.AnyAsync(x => x.RoleId == employeeRole && x.PermissionId == permissionId))
                    db.RolePermissions.Add(new RolePermission { RoleId = employeeRole, PermissionId = permissionId });
            }
            db.UserRoles.Add(new UserRole { TenantId = TenantId, UserId = EmployeeUserId, RoleId = employeeRole });
            var approveId = SeedData.PermissionId(Permissions.Leave.Approve);
            if (!await db.Permissions.AnyAsync(x => x.Id == approveId))
                db.Permissions.Add(new Permission { Id = approveId, Name = Permissions.Leave.Approve, Description = "Approve leave." });
            var managerRole = Random.Shared.Next(100_000, 900_000);
            db.Roles.Add(new Role { Id = managerRole, Name = $"M{TenantId:N}"[..12] });
            db.UserRoles.Add(new UserRole { TenantId = TenantId, UserId = ManagerUserId, RoleId = managerRole });
            db.RolePermissions.Add(new RolePermission { RoleId = managerRole, PermissionId = approveId });
            db.LeaveTypes.Add(new LeaveType { Id = LeaveTypeId, TenantId = TenantId, Code = $"L{TenantId:N}"[..12], Name = "Provider Leave", DefaultUnit = LeaveUnit.Day, IsActive = true });
            var periodId = Guid.NewGuid();
            var policyId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var ruleId = Guid.NewGuid();
            db.LeavePeriods.Add(new LeavePeriod { Id = periodId, TenantId = TenantId, Code = $"P{TenantId:N}"[..12], Name = "2026", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), IsActive = true });
            db.LeavePolicies.Add(new LeavePolicy { Id = policyId, TenantId = TenantId, Code = $"LP{TenantId:N}"[..12], Name = "Provider policy", IsActive = true });
            db.LeavePolicyVersions.Add(new LeavePolicyVersion { Id = versionId, TenantId = TenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), Status = LeavePolicyVersionStatus.Published, Priority = 1 });
            db.LeavePolicyRules.Add(new LeavePolicyRule { Id = ruleId, TenantId = TenantId, LeavePolicyVersionId = versionId, LeaveTypeId = LeaveTypeId, IsActive = true });
            db.LeavePolicyEntitlementRules.Add(new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = ruleId, EntitlementMode = EntitlementMode.Allocated, EntitlementSource = EntitlementSource.PolicyAccrual, EntitlementQuantity = 10, AccrualFrequency = AccrualFrequency.None });
            db.LeavePolicyCancellationRules.Add(new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = ruleId, WithdrawAllowed = true, CancelAllowed = true });
            db.EmployeeLeaveBalances.Add(new EmployeeLeaveBalance { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, LeaveTypeId = LeaveTypeId, LeavePeriodId = periodId, GrantedQuantity = 10 });
            await db.SaveChangesAsync();
        }

        private AccountEmployeeLinkEvent LinkEvent(Guid id, Guid userId, Guid employeeId) => new()
        {
            Id = id, TenantId = TenantId, SubjectUserId = userId, ActorUserId = userId, Sequence = 1,
            Operation = "Link", NewLinkId = id, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow,
            Reason = "Phase 6E SQL provider test", CorrelationId = Guid.NewGuid().ToString("N")
        };
    }
}
