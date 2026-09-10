using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Infrastructure.Security;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Sdk;

namespace HRMS.Tests;

/// <summary>
/// Real-MySQL lifecycle coverage for the existing Leave request services. The fixture owns one unique
/// tenant's rows in the disposable integration database and removes only those rows during cleanup.
/// </summary>
public sealed class MySqlLeaveLifecycleIntegrationTests
{
    [Fact]
    public async Task MySql_leave_approval_reminder_is_durable_and_duplicate_suppressed()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("MySQL Leave reminder test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using var context = fixture.CreateContext();
            var validation = new LeaveRequestValidationService(
                context,
                new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                new EffectiveEmploymentResolver(context, fixture.EmployeeTenant),
                new LeavePeriodResolver(context, fixture.EmployeeTenant),
                new LeavePolicyResolver(context, new EffectiveEmploymentResolver(context, fixture.EmployeeTenant), fixture.EmployeeTenant));
            var submitted = await new LeaveRequestSubmissionService(
                context,
                new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                validation,
                new MySqlLeaveRequestSubmissionLock(context),
                TimeProvider.System,
                balanceAccountingService: new LeaveBalanceAccountingService(context, fixture.EmployeeTenant, TimeProvider.System),
                notificationService: fixture.CreateNotificationService(context))
                .SubmitAsync(new(fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-reminder"));
            Assert.True(submitted.Succeeded);

            var request = await context.LeaveRequests.SingleAsync(x => x.Id == submitted.Value!.RequestId);
            var recentProcessor = new LeaveApprovalReminderProcessor(
                context,
                new EmployeeManagerResolver(context, fixture.EmployeeTenant),
                fixture.CreateNotificationService(context),
                new LeaveReminderOptions { InitialDelayHours = 24, RepeatIntervalHours = 24 },
                TimeProvider.System,
                NullLogger<LeaveApprovalReminderProcessor>.Instance);
            var recent = await recentProcessor.ProcessAsync();
            Assert.Equal(0, recent.Candidates);

            request.SubmittedAtUtc = DateTime.UtcNow.AddHours(-48);
            await context.SaveChangesAsync();
            fixture.Email.Messages.Clear();

            var options = new LeaveReminderOptions { InitialDelayHours = 24, RepeatIntervalHours = 24, BatchSize = 10, ClaimLeaseMinutes = 5 };
            var processor = new LeaveApprovalReminderProcessor(
                context,
                new EmployeeManagerResolver(context, fixture.EmployeeTenant),
                fixture.CreateNotificationService(context),
                options,
                TimeProvider.System,
                NullLogger<LeaveApprovalReminderProcessor>.Instance);

            var first = await processor.ProcessAsync();
            var second = await processor.ProcessAsync();

            Assert.Equal(1, first.Sent);
            Assert.Equal(0, second.Sent);
            Assert.Single(fixture.Email.Messages, message => message.Subject == "Leave approval reminder" && message.RecipientEmail == fixture.ManagerEmail);
            Assert.Equal(1, await context.LeaveReminderDeliveries.CountAsync(x => x.LeaveRequestId == submitted.Value.RequestId && x.Status == "Sent"));

            var approval = new LeaveRequestApprovalService(
                context,
                new EmployeeIdentityResolver(context, fixture.ManagerTenant),
                new EmployeeManagerResolver(context, fixture.ManagerTenant),
                new MySqlLeaveRequestSubmissionLock(context),
                TimeProvider.System,
                balanceAccountingService: new LeaveBalanceAccountingService(context, fixture.EmployeeTenant, TimeProvider.System));
            Assert.True((await approval.ApproveAsync(submitted.Value.RequestId)).Succeeded);
            var completed = await processor.ProcessAsync();
            Assert.Equal(0, completed.Candidates);
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_leave_state_remains_committed_when_email_delivery_fails()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("MySQL Leave notification test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            fixture.Email.ThrowOnSend = true;

            await using var context = fixture.CreateContext();
            var validation = new LeaveRequestValidationService(
                context,
                new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                new EffectiveEmploymentResolver(context, fixture.EmployeeTenant),
                new LeavePeriodResolver(context, fixture.EmployeeTenant),
                new LeavePolicyResolver(context, new EffectiveEmploymentResolver(context, fixture.EmployeeTenant), fixture.EmployeeTenant));
            var result = await new LeaveRequestSubmissionService(
                context,
                new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                validation,
                new MySqlLeaveRequestSubmissionLock(context),
                TimeProvider.System,
                balanceAccountingService: new LeaveBalanceAccountingService(context, fixture.EmployeeTenant, TimeProvider.System),
                notificationService: fixture.CreateNotificationService(context))
                .SubmitAsync(new(fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-notification-failure"));

            Assert.True(result.Succeeded);
            Assert.Equal(LeaveRequestStatus.PendingApproval, result.Value!.Status);
            Assert.Empty(fixture.Email.Messages);
            Assert.Equal(1m, await context.EmployeeLeaveBalances
                .Where(x => x.Id == fixture.BalanceId)
                .Select(x => x.ReservedQuantity)
                .SingleAsync());
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

    [Fact]
    public async Task MySql_leave_notification_does_not_guess_a_missing_manager_email()
    {
        var connection = Environment.GetEnvironmentVariable("HRMS_MYSQL_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection))
            throw SkipException.ForSkip("MySQL Leave notification test not executed: HRMS_MYSQL_TEST_CONNECTION is absent.");

        var fixture = new Fixture(connection);
        try
        {
            await fixture.SeedAsync();
            await using (var setup = fixture.CreateContext(new TestTenantContext()))
            {
                var manager = await setup.Users.IgnoreQueryFilters().SingleAsync(x => x.Id == fixture.ManagerUserId);
                manager.Email = string.Empty;
                await setup.SaveChangesAsync();
            }

            await using var context = fixture.CreateContext();
            var validation = new LeaveRequestValidationService(
                context,
                new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                new EffectiveEmploymentResolver(context, fixture.EmployeeTenant),
                new LeavePeriodResolver(context, fixture.EmployeeTenant),
                new LeavePolicyResolver(context, new EffectiveEmploymentResolver(context, fixture.EmployeeTenant), fixture.EmployeeTenant));
            var result = await new LeaveRequestSubmissionService(
                context,
                new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                validation,
                new MySqlLeaveRequestSubmissionLock(context),
                TimeProvider.System,
                balanceAccountingService: new LeaveBalanceAccountingService(context, fixture.EmployeeTenant, TimeProvider.System),
                notificationService: fixture.CreateNotificationService(context))
                .SubmitAsync(new(fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-missing-recipient"));

            Assert.True(result.Succeeded);
            Assert.Empty(fixture.Email.Messages);
        }
        finally
        {
            await fixture.CleanupAsync();
        }
    }

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
                    balanceAccountingService: accounting,
                    notificationService: fixture.CreateNotificationService(context));

                var submitted = await submission.SubmitAsync(new(
                    fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-lifecycle-submit"));
                Assert.True(submitted.Succeeded);
                Assert.Equal(LeaveRequestStatus.PendingApproval, submitted.Value!.Status);
                Assert.Equal(fixture.EmployeeId, submitted.Value.EmployeeId);
                Assert.Contains(fixture.Email.Messages, message => message.Subject == "Leave approval required" && message.RecipientEmail == fixture.ManagerEmail);

                var replay = await submission.SubmitAsync(new(
                    fixture.LeaveTypeId, fixture.RequestDate, fixture.RequestDate, "mysql-lifecycle-submit"));
                Assert.True(replay.Succeeded);
                Assert.True(replay.Value!.IdempotentReplay);
                Assert.Equal(submitted.Value.RequestId, replay.Value.RequestId);
                Assert.Equal(1, fixture.Email.Messages.Count(message => message.Subject == "Leave approval required"));

                var pendingManagerCalendar = await new LeaveCalendarService(
                    context,
                    new EmployeeIdentityResolver(context, fixture.ManagerTenant),
                    new EmployeeManagerResolver(context, fixture.ManagerTenant))
                    .GetAsync(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 2));
                Assert.True(pendingManagerCalendar.Succeeded);
                Assert.Contains(pendingManagerCalendar.Value!, item => item.RequestId == submitted.Value.RequestId &&
                    item.Status == LeaveRequestStatus.PendingApproval);

                var pendingEmployeeCalendar = await new LeaveCalendarService(
                    context,
                    new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                    new EmployeeManagerResolver(context, fixture.EmployeeTenant))
                    .GetAsync(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 2));
                Assert.True(pendingEmployeeCalendar.Succeeded);
                Assert.DoesNotContain(pendingEmployeeCalendar.Value!, item => item.RequestId == submitted.Value.RequestId);

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
                    balanceAccountingService: accounting,
                    notificationService: fixture.CreateNotificationService(context));
                var approved = await approval.ApproveAsync(submitted.Value.RequestId);
                Assert.True(approved.Succeeded);
                Assert.Equal(LeaveRequestStatus.Approved, approved.Value!.Status);
                Assert.Contains(fixture.Email.Messages, message => message.Subject == "Leave request approved" && message.RecipientEmail == fixture.EmployeeEmail);
                Assert.Equal(1, fixture.Email.Messages.Count(message => message.Subject == "Leave request approved"));

                var balanceAfterApproval = await context.EmployeeLeaveBalances
                    .SingleAsync(x => x.Id == fixture.BalanceId);
                Assert.Equal(0m, balanceAfterApproval.ReservedQuantity);
                Assert.Equal(1m, balanceAfterApproval.ConsumedQuantity);
                Assert.Equal(1, await context.LeaveBalanceTransactions.CountAsync(x =>
                    x.LeaveRequestId == submitted.Value.RequestId &&
                    x.TransactionType == LeaveBalanceTransactionType.Consumption));

                var managerCalendar = await new LeaveCalendarService(
                    context,
                    new EmployeeIdentityResolver(context, fixture.ManagerTenant),
                    new EmployeeManagerResolver(context, fixture.ManagerTenant))
                    .GetAsync(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 2));
                Assert.True(managerCalendar.Succeeded);
                Assert.Contains(managerCalendar.Value!, item => item.RequestId == submitted.Value.RequestId &&
                    item.EmployeeId == fixture.EmployeeId && item.Status == LeaveRequestStatus.Approved &&
                    item.EmployeeName == "Leave Employee" && item.LeaveTypeName == "Casual Leave");
                Assert.DoesNotContain(typeof(LeaveCalendarEventDto).GetProperties(), property =>
                    property.Name.Contains("Reason", StringComparison.OrdinalIgnoreCase) ||
                    property.Name.Contains("Comment", StringComparison.OrdinalIgnoreCase));

                var employeeCalendar = await new LeaveCalendarService(
                    context,
                    new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                    new EmployeeManagerResolver(context, fixture.EmployeeTenant))
                    .GetAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
                Assert.True(employeeCalendar.Succeeded);
                Assert.Contains(employeeCalendar.Value!, item => item.RequestId == submitted.Value.RequestId);
                Assert.DoesNotContain(employeeCalendar.Value!, item => item.Status == LeaveRequestStatus.PendingApproval);

                var outsideCalendar = await new LeaveCalendarService(
                    context,
                    new EmployeeIdentityResolver(context, fixture.ManagerTenant),
                    new EmployeeManagerResolver(context, fixture.ManagerTenant))
                    .GetAsync(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30));
                Assert.True(outsideCalendar.Succeeded);
                Assert.DoesNotContain(outsideCalendar.Value!, item => item.RequestId == submitted.Value.RequestId);

                var cancellation = new LeaveRequestCancellationService(
                    context,
                    new EmployeeIdentityResolver(context, fixture.EmployeeTenant),
                    new MySqlLeaveRequestSubmissionLock(context),
                    TimeProvider.System,
                    balanceAccountingService: accounting,
                    notificationService: fixture.CreateNotificationService(context));
                var cancelled = await cancellation.CancelAsync(submitted.Value.RequestId);
                Assert.True(cancelled.Succeeded);
                Assert.Equal(LeaveRequestStatus.Cancelled, cancelled.Value!.Status);
                Assert.Equal(1, fixture.Email.Messages.Count(message => message.Subject == "Leave request cancelled" && message.RecipientEmail == fixture.EmployeeEmail));
                Assert.Equal(1, fixture.Email.Messages.Count(message => message.Subject == "Leave request cancelled" && message.RecipientEmail == fixture.ManagerEmail));
                Assert.DoesNotContain(fixture.Email.Messages, message => message.Body.Contains(fixture.TenantId.ToString(), StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain(fixture.Email.Messages, message => message.Body.Contains(submitted.Value.RequestId.ToString(), StringComparison.OrdinalIgnoreCase));

                var cancellationReplay = await cancellation.CancelAsync(submitted.Value.RequestId);
                Assert.False(cancellationReplay.Succeeded);
                Assert.Equal(2, fixture.Email.Messages.Count(message => message.Subject == "Leave request cancelled"));

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

    internal sealed class Fixture
    {
        private readonly string _connection;
        public Guid TenantId { get; } = Guid.NewGuid();
        public Guid OtherTenantId { get; } = Guid.NewGuid();
        public string TenantCode => $"ML{TenantId:N}"[..8];
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
        public string Host => $"ml-{TenantId:N}.localhost";
        public string HttpHost => $"http://{Host}";
        public string EmployeeEmail => $"user-{EmployeeUserId:N}@test.invalid";
        public string ManagerEmail => $"manager-{ManagerUserId:N}@test.invalid";
        public const string Password = "Passw0rd!123";
        public RecordingLeaveEmailSender Email { get; } = new();

        public Fixture(string connection)
        {
            _connection = MySqlApiFactory.NormalizeConnectionString(connection);
            EmployeeTenant = new(TenantId, EmployeeUserId);
            ManagerTenant = new(TenantId, ManagerUserId);
        }

        public HrmsDbContext CreateContext(TestTenantContext? tenant = null) =>
            new(new DbContextOptionsBuilder<HrmsDbContext>()
                .UseMySQL(_connection)
                .AddInterceptors(new MySqlConcurrencyTokenInterceptor(new MySqlConcurrencyTokenGenerator()))
                .Options, tenant ?? EmployeeTenant);

        public ILeaveNotificationService CreateNotificationService(HrmsDbContext context) =>
            new LeaveNotificationService(context, Email, new EmployeeManagerResolver(context, EmployeeTenant), NullLogger<LeaveNotificationService>.Instance);

        public async Task SeedAsync()
        {
            await using var db = CreateContext(new TestTenantContext());
            db.Tenants.AddRange(
                new Tenant { Id = TenantId, TenantCode = TenantCode, TenantName = "MySQL Leave Lifecycle", Host = $"ml-{TenantId:N}.localhost", ShardKey = $"ml{TenantId:N}"[..10], Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql },
                new Tenant { Id = OtherTenantId, TenantCode = $"MO{OtherTenantId:N}"[..8], TenantName = "Other Tenant", Host = $"mo-{OtherTenantId:N}.localhost", ShardKey = $"mo{OtherTenantId:N}"[..10], Status = TenantStatus.Active, DatabaseProvider = DatabaseProviderType.MySql });

            var passwordHash = new IdentityPasswordHasher().Hash(Password);
            db.Users.AddRange(
                new User { Id = EmployeeUserId, TenantId = TenantId, Email = EmployeeEmail, PasswordHash = passwordHash, FirstName = "Leave", LastName = "Employee", IsActive = true },
                new User { Id = ManagerUserId, TenantId = TenantId, Email = ManagerEmail, PasswordHash = passwordHash, FirstName = "Leave", LastName = "Manager", IsActive = true });
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
            var submission = new LeaveRequestSubmissionService(db, new EmployeeIdentityResolver(db, EmployeeTenant), validation, new MySqlLeaveRequestSubmissionLock(db), TimeProvider.System, balanceAccountingService: accounting, notificationService: CreateNotificationService(db));
            var rejected = await submission.SubmitAsync(new(LeaveTypeId, RequestDate.AddDays(1), RequestDate.AddDays(1), "mysql-lifecycle-reject"));
            Assert.True(rejected.Succeeded);
            var approval = new LeaveRequestApprovalService(db, new EmployeeIdentityResolver(db, ManagerTenant), new EmployeeManagerResolver(db, ManagerTenant), new MySqlLeaveRequestSubmissionLock(db), TimeProvider.System, balanceAccountingService: accounting, notificationService: CreateNotificationService(db));
            var rejection = await approval.RejectAsync(rejected.Value!.RequestId);
            Assert.True(rejection.Succeeded);
            Assert.Contains(Email.Messages, message => message.Subject == "Leave request rejected" && message.RecipientEmail == EmployeeEmail);
            Assert.Equal(1, Email.Messages.Count(message => message.Subject == "Leave request rejected"));
            var rejectionReplay = await approval.RejectAsync(rejected.Value.RequestId);
            Assert.False(rejectionReplay.Succeeded);
            Assert.Equal(1, Email.Messages.Count(message => message.Subject == "Leave request rejected"));
            var withdrawn = await submission.SubmitAsync(new(LeaveTypeId, RequestDate.AddDays(2), RequestDate.AddDays(2), "mysql-lifecycle-withdraw"));
            Assert.True(withdrawn.Succeeded);
            var withdrawal = new LeaveRequestWithdrawalService(db, new EmployeeIdentityResolver(db, EmployeeTenant), new MySqlLeaveRequestSubmissionLock(db), TimeProvider.System, balanceAccountingService: accounting, notificationService: CreateNotificationService(db));
            var result = await withdrawal.WithdrawAsync(withdrawn.Value!.RequestId);
            Assert.True(result.Succeeded);
            Assert.Equal(LeaveRequestStatus.Withdrawn, result.Value!.Status);
            Assert.Contains(Email.Messages, message => message.Subject == "Leave request withdrawn" && message.RecipientEmail == ManagerEmail);
            Assert.Equal(1, Email.Messages.Count(message => message.Subject == "Leave request withdrawn"));
            var withdrawalReplay = await withdrawal.WithdrawAsync(withdrawn.Value.RequestId);
            Assert.False(withdrawalReplay.Succeeded);
            Assert.Equal(1, Email.Messages.Count(message => message.Subject == "Leave request withdrawn"));
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
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `LeaveReminderDeliveries` WHERE `TenantId` = {TenantId}");
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

    internal sealed class RecordingLeaveEmailSender : IEmailSender
    {
        public List<LeaveNotificationEmailMessage> Messages { get; } = [];
        public bool ThrowOnSend { get; set; }
        public Task SendLeaveNotificationAsync(LeaveNotificationEmailMessage message, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSend) throw new InvalidOperationException("test email provider failure");
            Messages.Add(message);
            return Task.CompletedTask;
        }
        public Task SendWelcomeInviteAsync(WelcomeEmailMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string?> SendPasswordResetOtpAsync(OtpDeliveryMessage message, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }
}
