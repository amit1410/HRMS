using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit.Sdk;

namespace HRMS.Tests;

/// <summary>
/// Real-MySQL lifecycle coverage for the existing Leave request services. The fixture owns one unique
/// tenant's rows in the disposable integration database and removes only those rows during cleanup.
/// </summary>
public sealed class MySqlLeaveLifecycleIntegrationTests
{
    [Fact]
    public async Task MySql_leave_lifecycle_uses_linked_identity_policy_and_atomic_balance_accounting()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("MySQL Leave lifecycle test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new Fixture(connection);
        try
        {
            await fixture.SeedAsync();

            await using (var context = fixture.CreateContext())
            {
                var identity = await new EmployeeIdentityResolver(context, fixture.EmployeeTenant)
                    .ResolveCurrentAsync();
                Assert.True(identity.Succeeded);
                Assert.Equal(fixture.EmployeeId, identity.Value!.EmployeeId);
                Assert.NotEqual(fixture.EmployeeUserId, identity.Value.EmployeeId);

                var manager = await new EmployeeManagerResolver(context, fixture.EmployeeTenant)
                    .ResolveAsync(fixture.EmployeeId, fixture.RequestDate);
                Assert.True(manager.Succeeded);
                Assert.Equal(EmployeeManagerResolutionStatus.Resolved, manager.Value!.Status);
                Assert.Equal(fixture.ManagerId, manager.Value.ManagerId);

                var policy = await new LeavePolicyResolver(
                    context,
                    new EffectiveEmploymentResolver(context, fixture.EmployeeTenant),
                    fixture.EmployeeTenant)
                    .ResolveAsync(fixture.TenantId, fixture.EmployeeId, fixture.LeaveTypeId, fixture.RequestDate);
                Assert.Equal(LeavePolicyResolutionStatus.Resolved, policy.Status);
                Assert.Equal(fixture.PolicyVersionId, policy.LeavePolicyVersionId);
                Assert.Equal(fixture.PolicyRuleId, policy.LeavePolicyRuleId);

                var validation = new LeaveRequestValidationService(
                    context,
                    new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                    new EffectiveEmploymentResolver(context, fixture.EmployeeTenant),
                    new LeavePeriodResolver(context, fixture.EmployeeTenant),
                    new LeavePolicyResolver(context, new EffectiveEmploymentResolver(context, fixture.EmployeeTenant), fixture.EmployeeTenant));
                var preview = await validation.ValidateAsync(new(
                    fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-lifecycle-preview"));
                Assert.True(preview.Succeeded);
                Assert.Equal(1m, preview.Value!.ChargeableQuantity);

                var invalid = await validation.ValidateAsync(new(
                    fixture.LeaveTypeId, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1), "mysql-lifecycle-invalid"));
                Assert.False(invalid.Succeeded);

                var accounting = new LeaveBalanceAccountingService(context, fixture.EmployeeTenant, TimeProvider.System);
                var submission = new LeaveRequestSubmissionService(
                    context,
                    new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                    validation,
                    new MySqlLeaveRequestSubmissionLock(context),
                    TimeProvider.System,
                    balanceAccountingService: accounting);

                var submitted = await submission.SubmitAsync(new(
                    fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-lifecycle-submit"));
                Assert.True(submitted.Succeeded);
                Assert.Equal(LeaveRequestStatus.PendingApproval, submitted.Value!.Status);
                Assert.Equal(fixture.EmployeeId, submitted.Value.EmployeeId);

                var replay = await submission.SubmitAsync(new(
                    fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-lifecycle-submit"));
                Assert.True(replay.Succeeded);
                Assert.True(replay.Value!.IdempotentReplay);
                Assert.Equal(submitted.Value.RequestId, replay.Value.RequestId);

                var balanceAfterSubmit = await context.EmployeeLeaveBalances
                    .SingleAsync(x => x.Id == fixture.BalanceId);
                Assert.Equal(1m, balanceAfterSubmit.ReservedQuantity);
                Assert.Equal(0m, balanceAfterSubmit.ConsumedQuantity);

                var approval = new LeaveRequestApprovalService(
                    context,
                    new EmployeeIdentityResolver(context, fixture.ManagerTenant),
                    new EmployeeManagerResolver(context, fixture.ManagerTenant),
                    new MySqlLeaveRequestSubmissionLock(context),
                    TimeProvider.System,
                    balanceAccountingService: accounting);
                var approved = await approval.ApproveAsync(submitted.Value.RequestId);
                Assert.True(approved.Succeeded);
                Assert.Equal(LeaveRequestStatus.Approved, approved.Value!.Status);

                var balanceAfterApproval = await context.EmployeeLeaveBalances
                    .SingleAsync(x => x.Id == fixture.BalanceId);
                Assert.Equal(0m, balanceAfterApproval.ReservedQuantity);
                Assert.Equal(1m, balanceAfterApproval.ConsumedQuantity);
                Assert.Equal(1, await context.LeaveBalanceTransactions.CountAsync(x =>
                    x.LeaveRequestId == submitted.Value.RequestId &&
                    x.TransactionType == LeaveBalanceTransactionType.Consumption));

                var cancellation = new LeaveRequestCancellationService(
                    context,
                    new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                    new MySqlLeaveRequestSubmissionLock(context),
                    TimeProvider.System,
                    balanceAccountingService: accounting);
                var cancelled = await cancellation.CancelAsync(submitted.Value.RequestId);
                Assert.True(cancelled.Succeeded);
                Assert.Equal(LeaveRequestStatus.Cancelled, cancelled.Value!.Status);

                var balanceAfterCancellation = await context.EmployeeLeaveBalances
                    .SingleAsync(x => x.Id == fixture.BalanceId);
                Assert.Equal(0m, balanceAfterCancellation.ConsumedQuantity);
                Assert.Equal(1, await context.LeaveBalanceTransactions.CountAsync(x =>
                    x.LeaveRequestId == submitted.Value.RequestId &&
                    x.TransactionType == LeaveBalanceTransactionType.CancellationRestore));
            }

            await fixture.RunRejectionAndWithdrawalAsync();
            await fixture.AssertTenantIsolationAsync();

            await using (var verification = fixture.CreateContext())
            {
                var balance = await verification.EmployeeLeaveBalances.SingleAsync(x => x.Id == fixture.BalanceId);
                Assert.Equal(0m, balance.ReservedQuantity);
                Assert.Equal(0m, balance.ConsumedQuantity);
                Assert.Equal(3, await verification.LeaveBalanceTransactions.CountAsync(x => x.EmployeeLeaveBalanceId == fixture.BalanceId && x.TransactionType == LeaveBalanceTransactionType.Reservation));
                Assert.Equal(2, await verification.LeaveBalanceTransactions.CountAsync(x => x.EmployeeLeaveBalanceId == fixture.BalanceId && x.TransactionType == LeaveBalanceTransactionType.ReservationRelease));
                Assert.Equal(1, await verification.LeaveBalanceTransactions.CountAsync(x => x.EmployeeLeaveBalanceId == fixture.BalanceId && x.TransactionType == LeaveBalanceTransactionType.Consumption));
                Assert.Equal(1, await verification.LeaveBalanceTransactions.CountAsync(x => x.EmployeeLeaveBalanceId == fixture.BalanceId && x.TransactionType == LeaveBalanceTransactionType.CancellationRestore));
            }
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    private sealed class Fixture
    {
        private readonly string _connection;
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid OtherTenantId { get; } = Guid.NewGuid();
        public Guid EmployeeId { get; } = Guid.NewGuid();
        public Guid ManagerId { get; } = Guid.NewGuid();
        public Guid EmployeeUserId { get; } = Guid.NewGuid();
        public Guid ManagerUserId { get; } = Guid.NewGuid();
        public Guid LeaveTypeId { get; } = Guid.NewGuid();
        public Guid LeavePeriodId { get; } = Guid.NewGuid();
        public Guid PolicyId { get; } = Guid.NewGuid();
        public Guid PolicyVersionId { get; } = Guid.NewGuid();
        public Guid PolicyRuleId { get; } = Guid.NewGuid();
        public Guid EmployeeHistoryId { get; } = Guid.NewGuid();
        public Guid ManagerHistoryId { get; } = Guid.NewGuid();
        public Guid BalanceId { get; } = Guid.NewGuid();
        public int RoleId { get; } = Random.Shared.Next(100_000, 900_000);
        public int PermissionId { get; private set; }
        public Guid EmployeeLinkId { get; } = Guid.NewGuid();
        public Guid ManagerLinkId { get; } = Guid.NewGuid();
        public DateOnly RequestDate { get; } = new(2026, 10, 1);
        public TestTenantContext EmployeeTenant { get; }
        public TestTenantContext ManagerTenant { get; }

        public Fixture(string connection)
        {
            _connection = connection;
            EmployeeTenant = new(TenantId, EmployeeUserId);
            ManagerTenant = new(TenantId, ManagerUserId);
        }

        public HrmsDbContext CreateContext(TestTenantContext? tenant = null) =>
            new(new DbContextOptionsBuilder<HrmsDbContext>()
                .UseMySQL(_connection)
                .AddInterceptors(new MySqlConcurrencyTokenInterceptor(new MySqlConcurrencyTokenGenerator()))
                .Options, tenant ?? EmployeeTenant);

        public async Task SeedAsync()
        {
            await using var db = CreateContext(new TestTenantContext());
            db.Tenants.AddRange(
                new Tenant { Id = TenantId, TenantCode = $"ML{TenantId:N}"[..8], TenantName = "MySQL Leave Lifecycle", Host = $"ml-{TenantId:N}.localhost", ShardKey = $"ml{TenantId:N}"[..10], Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql },
                new Tenant { Id = OtherTenantId, TenantCode = $"MO{OtherTenantId:N}"[..8], TenantName = "Other Tenant", Host = $"mo-{OtherTenantId:N}.localhost", ShardKey = $"mo{OtherTenantId:N}"[..10], Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql });

            db.Users.AddRange(
                new User { Id = EmployeeUserId, TenantId = TenantId, Email = $"user-{EmployeeUserId:N}@test.invalid", PasswordHash = "test", FirstName = "Leave", LastName = "Employee", IsActive = true },
                new User { Id = ManagerUserId, TenantId = TenantId, Email = $"manager-{ManagerUserId:N}@test.invalid", PasswordHash = "test", FirstName = "Leave", LastName = "Manager", IsActive = true });
            db.Employees.AddRange(
                new Employee { Id = EmployeeId, TenantId = TenantId, EmployeeCode = $"E{EmployeeId:N}"[..8], FirstName = "Leave", LastName = "Employee", Email = $"employee-{EmployeeId:N}@test.invalid", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active, ReportingManagerId = ManagerId },
                new Employee { Id = ManagerId, TenantId = TenantId, EmployeeCode = $"M{ManagerId:N}"[..8], FirstName = "Leave", LastName = "Manager", Email = $"manager-{ManagerId:N}@test.invalid", DateOfJoining = new(2020, 1, 1), Status = EmployeeStatus.Active });
            db.EmployeeEmploymentHistory.AddRange(
                new EmployeeEmploymentHistory { Id = EmployeeHistoryId, TenantId = TenantId, EmployeeId = EmployeeId, EffectiveFrom = new(2020, 1, 1), ManagerId = ManagerId, EmploymentStatus = EmployeeStatus.Active },
                new EmployeeEmploymentHistory { Id = ManagerHistoryId, TenantId = TenantId, EmployeeId = ManagerId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            db.AccountEmployeeCurrentLinks.AddRange(
                new AccountEmployeeCurrentLink { LinkId = EmployeeLinkId, TenantId = TenantId, UserId = EmployeeUserId, EmployeeId = EmployeeId },
                new AccountEmployeeCurrentLink { LinkId = ManagerLinkId, TenantId = TenantId, UserId = ManagerUserId, EmployeeId = ManagerId });
            db.AccountEmployeeLinkEvents.AddRange(
                LinkEvent(EmployeeLinkId, EmployeeUserId, EmployeeId, "employee-link"),
                LinkEvent(ManagerLinkId, ManagerUserId, ManagerId, "manager-link"));
            var approvePermission = await db.Permissions.SingleAsync(x => x.Name == Permissions.Leave.Approve);
            PermissionId = approvePermission.Id;
            db.Roles.Add(new Role { Id = RoleId, Name = $"M{TenantId:N}"[..8] });
            db.UserRoles.Add(new UserRole { TenantId = TenantId, UserId = ManagerUserId, RoleId = RoleId });
            db.RolePermissions.Add(new RolePermission { RoleId = RoleId, PermissionId = PermissionId });
            db.LeaveTypes.Add(new LeaveType { Id = LeaveTypeId, TenantId = TenantId, Code = $"CL{TenantId:N}"[..8], Name = "Casual Leave", DefaultUnit = LeaveUnit.Day, IsActive = true });
            db.LeavePeriods.Add(new LeavePeriod { Id = LeavePeriodId, TenantId = TenantId, Code = $"FY{TenantId:N}"[..8], Name = "2026", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), IsActive = true });
            db.LeavePolicies.Add(new LeavePolicy { Id = PolicyId, TenantId = TenantId, Code = $"LP{TenantId:N}"[..8], Name = "Standard Leave Policy", IsActive = true });
            db.LeavePolicyVersions.Add(new LeavePolicyVersion { Id = PolicyVersionId, TenantId = TenantId, LeavePolicyId = PolicyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), EffectiveTo = new(2026, 12, 31), Status = LeavePolicyVersionStatus.Published, Priority = 1 });
            db.LeavePolicyRules.Add(new LeavePolicyRule { Id = PolicyRuleId, TenantId = TenantId, LeavePolicyVersionId = PolicyVersionId, LeaveTypeId = LeaveTypeId, IsActive = true });
            db.LeavePolicyEntitlementRules.Add(new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = PolicyRuleId, EntitlementMode = EntitlementMode.Allocated, EntitlementSource = EntitlementSource.PolicyAccrual, EntitlementQuantity = 10, AccrualFrequency = AccrualFrequency.None });
            db.LeavePolicyCancellationRules.Add(new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = PolicyRuleId, WithdrawAllowed = true, CancelAllowed = true });
            db.EmployeeLeaveBalances.Add(new EmployeeLeaveBalance { Id = BalanceId, TenantId = TenantId, EmployeeId = EmployeeId, LeaveTypeId = LeaveTypeId, LeavePeriodId = LeavePeriodId, GrantedQuantity = 10, ReservedQuantity = 0, ConsumedQuantity = 0 });
            await db.SaveChangesAsync();
        }

        private AccountEmployeeLinkEvent LinkEvent(Guid linkId, Guid userId, Guid employeeId, string correlation) => new()
        {
            Id = linkId, TenantId = TenantId, SubjectUserId = userId, ActorUserId = userId, Sequence = 1,
            Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId, OccurredAtUtc = DateTime.UtcNow,
            Reason = "MySQL lifecycle test", CorrelationId = correlation
        };

        public async Task RunRejectionAndWithdrawalAsync()
        {
            await using var db = CreateContext();
            var accounting = new LeaveBalanceAccountingService(db, EmployeeTenant, TimeProvider.System);
            var validation = new LeaveRequestValidationService(db, new EmployeeIdentityResolver(db, EmployeeTenant), new EffectiveEmploymentResolver(db, EmployeeTenant), new LeavePeriodResolver(db, EmployeeTenant), new LeavePolicyResolver(db, new EffectiveEmploymentResolver(db, EmployeeTenant), EmployeeTenant));
            var submission = new LeaveRequestSubmissionService(db, new EmployeeIdentityResolver(db, EmployeeTenant), validation, new MySqlLeaveRequestSubmissionLock(db), TimeProvider.System, balanceAccountingService: accounting);
            var rejected = await submission.SubmitAsync(new(LeaveTypeId, RequestDate.AddDays(1), RequestDate.AddDays(1), "mysql-lifecycle-reject"));
            Assert.True(rejected.Succeeded);
            var approval = new LeaveRequestApprovalService(db, new EmployeeIdentityResolver(db, ManagerTenant), new EmployeeManagerResolver(db, ManagerTenant), new MySqlLeaveRequestSubmissionLock(db), TimeProvider.System, balanceAccountingService: accounting);
            var rejection = await approval.RejectAsync(rejected.Value!.RequestId);
            Assert.True(rejection.Succeeded);
            var withdrawn = await submission.SubmitAsync(new(LeaveTypeId, RequestDate.AddDays(2), RequestDate.AddDays(2), "mysql-lifecycle-withdraw"));
            Assert.True(withdrawn.Succeeded);
            var withdrawal = new LeaveRequestWithdrawalService(db, new EmployeeIdentityResolver(db, EmployeeTenant), new MySqlLeaveRequestSubmissionLock(db), TimeProvider.System, balanceAccountingService: accounting);
            var result = await withdrawal.WithdrawAsync(withdrawn.Value!.RequestId);
            Assert.True(result.Succeeded);
            Assert.Equal(LeaveRequestStatus.Withdrawn, result.Value!.Status);
        }

        public async Task AssertTenantIsolationAsync()
        {
            await using var db = CreateContext(new TestTenantContext(OtherTenantId, Guid.NewGuid()));
            Assert.False(await db.LeaveTypes.AnyAsync(x => x.Id == LeaveTypeId));
            Assert.False(await db.LeaveRequests.AnyAsync(x => x.EmployeeId == EmployeeId));
        }

        public async Task CleanupAsync()
        {
            await using var db = CreateContext(new TestTenantContext());
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveRequestEvents` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveRequestDays` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveBalanceTransactions` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveRequests` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `EmployeeLeaveBalances` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyCancellationRules` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyEntitlementRules` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyRules` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicyVersions` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePolicies` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeavePeriods` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveTypes` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `AccountEmployeeCurrentLinks` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `AccountEmployeeLinkEvents` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `UserRoles` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `RolePermissions` WHERE `RoleId` = {RoleId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Roles` WHERE `Id` = {RoleId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `EmployeeEmploymentHistory` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE `Employees` SET `ReportingManagerId` = NULL WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Employees` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Users` WHERE `TenantId` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Tenants` WHERE `Id` = {TenantId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Tenants` WHERE `Id` = {OtherTenantId}");
        }
    }
}
