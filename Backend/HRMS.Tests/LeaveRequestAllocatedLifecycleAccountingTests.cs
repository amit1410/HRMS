using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class LeaveRequestAllocatedLifecycleAccountingTests
{
    [Fact]
    public async Task Approval_consumes_the_pending_reservation()
    {
        using var fixture = await LifecycleFixture.CreateAsync();

        var result = await fixture.ApproveAsync();
        var state = await fixture.ReadAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(LeaveRequestStatus.Approved, state.Request.Status);
        Assert.Equal(0m, state.Balance.ReservedQuantity);
        Assert.Equal(5m, state.Balance.ConsumedQuantity);
        Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Reservation);
        var consumption = Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Consumption);
        Assert.Equal(3m, consumption.Quantity);
        Assert.Equal(fixture.RequestId, consumption.LeaveRequestId);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Approved);
    }

    [Fact]
    public async Task Rejection_releases_the_pending_reservation()
    {
        using var fixture = await LifecycleFixture.CreateAsync();

        var result = await fixture.RejectAsync();
        var state = await fixture.ReadAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(LeaveRequestStatus.Rejected, state.Request.Status);
        Assert.Equal(0m, state.Balance.ReservedQuantity);
        Assert.Equal(2m, state.Balance.ConsumedQuantity);
        var release = Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.ReservationRelease);
        Assert.Equal(3m, release.Quantity);
        Assert.Equal(fixture.RequestId, release.LeaveRequestId);
        Assert.DoesNotContain(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Consumption);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Rejected);
    }

    [Fact]
    public async Task Withdrawal_releases_the_owner_reservation_without_manager_authorization()
    {
        using var fixture = await LifecycleFixture.CreateAsync();

        var result = await fixture.WithdrawAsync();
        var state = await fixture.ReadAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(LeaveRequestStatus.Withdrawn, state.Request.Status);
        Assert.Equal(0m, state.Balance.ReservedQuantity);
        Assert.Equal(2m, state.Balance.ConsumedQuantity);
        var release = Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.ReservationRelease);
        Assert.Equal(3m, release.Quantity);
        Assert.DoesNotContain(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Consumption);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Withdrawn);
    }

    [Fact]
    public async Task Approval_without_a_sufficient_reservation_fails_without_accounting()
    {
        using var fixture = await LifecycleFixture.CreateAsync(reserved: 1m);

        var result = await fixture.ApproveAsync();
        var state = await fixture.ReadAsync();

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("AllocatedReservationNotFound", result.Message);
        Assert.Equal(LeaveRequestStatus.PendingApproval, state.Request.Status);
        Assert.Equal(1m, state.Balance.ReservedQuantity);
        Assert.Equal(2m, state.Balance.ConsumedQuantity);
        Assert.DoesNotContain(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Consumption);
        Assert.DoesNotContain(state.Events, x => x.EventType == LeaveRequestEventType.Approved);
    }

    [Fact]
    public async Task Approve_then_approve_has_one_consumption_and_one_terminal_event()
    {
        using var fixture = await LifecycleFixture.CreateAsync();

        var first = await fixture.ApproveAsync();
        var second = await fixture.ApproveAsync();
        var state = await fixture.ReadAsync();

        Assert.True(first.Succeeded, first.Message);
        Assert.Equal(ResultStatus.Conflict, second.Status);
        Assert.Contains("InvalidStatusTransition", second.Message);
        Assert.Equal(5m, state.Balance.ConsumedQuantity);
        Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Consumption);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Approved);
    }

    [Fact]
    public async Task Approve_then_reject_cannot_release_consumed_leave()
    {
        using var fixture = await LifecycleFixture.CreateAsync();

        Assert.True((await fixture.ApproveAsync()).Succeeded);
        var reject = await fixture.RejectAsync();
        var state = await fixture.ReadAsync();

        Assert.Equal(ResultStatus.Conflict, reject.Status);
        Assert.Contains("InvalidStatusTransition", reject.Message);
        Assert.Equal(5m, state.Balance.ConsumedQuantity);
        Assert.DoesNotContain(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.ReservationRelease);
    }

    [Fact]
    public async Task Reject_then_approve_cannot_consume_released_leave()
    {
        using var fixture = await LifecycleFixture.CreateAsync();

        Assert.True((await fixture.RejectAsync()).Succeeded);
        var approve = await fixture.ApproveAsync();
        var state = await fixture.ReadAsync();

        Assert.Equal(ResultStatus.Conflict, approve.Status);
        Assert.Contains("InvalidStatusTransition", approve.Message);
        Assert.Equal(0m, state.Balance.ReservedQuantity);
        Assert.Equal(2m, state.Balance.ConsumedQuantity);
        Assert.DoesNotContain(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Consumption);
    }

    [Fact]
    public async Task Withdraw_then_approve_cannot_consume_released_leave()
    {
        using var fixture = await LifecycleFixture.CreateAsync();

        Assert.True((await fixture.WithdrawAsync()).Succeeded);
        var approve = await fixture.ApproveAsync();
        var state = await fixture.ReadAsync();

        Assert.Equal(ResultStatus.Conflict, approve.Status);
        Assert.Contains("InvalidStatusTransition", approve.Message);
        Assert.Equal(0m, state.Balance.ReservedQuantity);
        Assert.Equal(2m, state.Balance.ConsumedQuantity);
        Assert.DoesNotContain(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Consumption);
    }

    private sealed class FixedIdentity(Result<RuntimeEmployeeIdentity> result) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class NoOpLock : ILeaveRequestSubmissionLock
    {
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class LifecycleFixture : IDisposable
    {
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly Guid OwnerUserId = Guid.NewGuid();
        public readonly Guid ManagerUserId = Guid.NewGuid();
        public readonly Guid OwnerEmployeeId = Guid.NewGuid();
        public readonly Guid ManagerEmployeeId = Guid.NewGuid();
        public readonly Guid LeaveTypeId = Guid.NewGuid();
        public readonly Guid LeavePeriodId = Guid.NewGuid();
        public readonly Guid PolicyId = Guid.NewGuid();
        public readonly Guid PolicyVersionId = Guid.NewGuid();
        public readonly Guid PolicyRuleId = Guid.NewGuid();
        public readonly Guid EmploymentId = Guid.NewGuid();
        public readonly Guid RequestId = Guid.NewGuid();
        private readonly Guid _balanceId = Guid.NewGuid();
        private readonly SqliteInMemoryDatabase _database;
        private readonly List<HrmsDbContext> _contexts = [];

        private LifecycleFixture(SqliteInMemoryDatabase database) => _database = database;

        public static async Task<LifecycleFixture> CreateAsync(decimal reserved = 3m)
        {
            var fixture = new LifecycleFixture(new SqliteInMemoryDatabase());
            await fixture.SeedAsync(reserved);
            return fixture;
        }

        public Task<Result<LeaveRequestApprovalResult>> ApproveAsync() =>
            ApprovalService().ApproveAsync(RequestId);

        public Task<Result<LeaveRequestApprovalResult>> RejectAsync() =>
            ApprovalService().RejectAsync(RequestId);

        public Task<Result<LeaveRequestWithdrawalResult>> WithdrawAsync() =>
            WithdrawalService().WithdrawAsync(RequestId);

        private LeaveRequestApprovalService ApprovalService()
        {
            var context = _database.CreateContext(new TestTenantContext(TenantId));
            _contexts.Add(context);
            var tenantContext = new TestTenantContext(TenantId);
            return new LeaveRequestApprovalService(
                context,
                new FixedIdentity(Result<RuntimeEmployeeIdentity>.Success(new(TenantId, ManagerUserId, ManagerEmployeeId))),
                new EmployeeManagerResolver(context, tenantContext),
                new NoOpLock(),
                TimeProvider.System,
                balanceAccountingService: new LeaveBalanceAccountingService(context, tenantContext, TimeProvider.System));
        }

        private LeaveRequestWithdrawalService WithdrawalService()
        {
            var context = _database.CreateContext(new TestTenantContext(TenantId));
            _contexts.Add(context);
            var tenantContext = new TestTenantContext(TenantId);
            return new LeaveRequestWithdrawalService(
                context,
                new FixedIdentity(Result<RuntimeEmployeeIdentity>.Success(new(TenantId, OwnerUserId, OwnerEmployeeId))),
                new NoOpLock(),
                TimeProvider.System,
                balanceAccountingService: new LeaveBalanceAccountingService(context, tenantContext, TimeProvider.System));
        }

        public async Task<State> ReadAsync()
        {
            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            return new State(
                await context.LeaveRequests.SingleAsync(x => x.Id == RequestId),
                await context.EmployeeLeaveBalances.SingleAsync(x => x.Id == _balanceId),
                await context.LeaveBalanceTransactions.Where(x => x.LeaveRequestId == RequestId).ToListAsync(),
                await context.LeaveRequestEvents.Where(x => x.LeaveRequestId == RequestId).ToListAsync());
        }

        private async Task SeedAsync(decimal reserved)
        {
            await using var context = _database.CreateContext(new TestTenantContext());
            context.AddRange(
                new Tenant { Id = TenantId, TenantCode = TenantId.ToString("N")[..8], Host = $"{TenantId}.local", ShardKey = TenantId.ToString("N"), TenantName = "Test" },
                new User { Id = OwnerUserId, TenantId = TenantId, Email = $"owner-{OwnerUserId}@test.local", PasswordHash = "test", FirstName = "Owner", LastName = "Employee" },
                new User { Id = ManagerUserId, TenantId = TenantId, Email = $"manager-{ManagerUserId}@test.local", PasswordHash = "test", FirstName = "Manager", LastName = "Employee" },
                new Role { Id = 700, Name = "AllocatedLifecycleApprover" },
                new Permission { Id = 35, Name = Permissions.Leave.Approve },
                new UserRole { UserId = ManagerUserId, RoleId = 700, TenantId = TenantId },
                new RolePermission { RoleId = 700, PermissionId = 35 },
                new Employee { Id = OwnerEmployeeId, TenantId = TenantId, EmployeeCode = "EMP-1", FirstName = "Owner", LastName = "Employee", Email = $"owner-employee-{OwnerEmployeeId}@test.local", DateOfJoining = new(2020, 1, 1), ReportingManagerId = ManagerEmployeeId },
                new Employee { Id = ManagerEmployeeId, TenantId = TenantId, EmployeeCode = "MGR-1", FirstName = "Manager", LastName = "Employee", Email = $"manager-employee-{ManagerEmployeeId}@test.local", DateOfJoining = new(2020, 1, 1) },
                new EmployeeEmploymentHistory { Id = EmploymentId, TenantId = TenantId, EmployeeId = OwnerEmployeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active, ManagerId = ManagerEmployeeId },
                new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = ManagerEmployeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active },
                new LeaveType { Id = LeaveTypeId, TenantId = TenantId, Code = "AL", Name = "Annual Leave" },
                new LeavePeriod { Id = LeavePeriodId, TenantId = TenantId, Code = "2027", Name = "2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) },
                new LeavePolicy { Id = PolicyId, TenantId = TenantId, Code = "POL", Name = "Policy" },
                new LeavePolicyVersion { Id = PolicyVersionId, TenantId = TenantId, LeavePolicyId = PolicyId, VersionNumber = 1, EffectiveFrom = new(2027, 1, 1), Status = LeavePolicyVersionStatus.Published },
                new LeavePolicyRule { Id = PolicyRuleId, TenantId = TenantId, LeavePolicyVersionId = PolicyVersionId, LeaveTypeId = LeaveTypeId },
                new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = PolicyRuleId, EntitlementMode = EntitlementMode.Allocated },
                new LeaveRequest { Id = RequestId, TenantId = TenantId, EmployeeId = OwnerEmployeeId, LeaveTypeId = LeaveTypeId, LeavePeriodId = LeavePeriodId, LeavePolicyVersionId = PolicyVersionId, LeavePolicyRuleId = PolicyRuleId, EmployeeEmploymentHistoryId = EmploymentId, StartDate = new(2027, 1, 2), EndDate = new(2027, 1, 2), RequestedQuantity = 3, ChargeableQuantity = 3, Status = LeaveRequestStatus.PendingApproval, SubmittedAtUtc = new(2026, 12, 1), IdempotencyKey = RequestId.ToString("N"), PayloadFingerprint = new string('a', 64) });
            context.EmployeeLeaveBalances.Add(new EmployeeLeaveBalance { Id = _balanceId, TenantId = TenantId, EmployeeId = OwnerEmployeeId, LeaveTypeId = LeaveTypeId, LeavePeriodId = LeavePeriodId, GrantedQuantity = 9, ReservedQuantity = reserved, ConsumedQuantity = 2 });
            await context.SaveChangesAsync();
            context.LeaveRequestEvents.Add(new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = RequestId, EventType = LeaveRequestEventType.Submitted, OccurredAtUtc = new(2026, 12, 1), ActorType = LeaveBalanceActorType.User, ActorUserId = OwnerUserId, ActorEmployeeId = OwnerEmployeeId });
            context.LeaveBalanceTransactions.Add(new LeaveBalanceTransaction { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeLeaveBalanceId = _balanceId, EmployeeId = OwnerEmployeeId, LeaveTypeId = LeaveTypeId, LeavePeriodId = LeavePeriodId, LeaveRequestId = RequestId, TransactionType = LeaveBalanceTransactionType.Reservation, Quantity = 3, EffectiveDate = new(2027, 1, 2), OccurredAtUtc = new(2026, 12, 1), LeavePolicyVersionId = PolicyVersionId, LeavePolicyRuleId = PolicyRuleId, SourceType = LeaveBalanceSourceType.Policy, SourceReference = $"LeaveRequest:{RequestId:D}", ActorType = LeaveBalanceActorType.User, ActorUserId = OwnerUserId, ActorEmployeeId = OwnerEmployeeId, IdempotencyKey = $"seed:{RequestId:N}", PayloadFingerprint = new string('b', 64) });
            await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            foreach (var context in _contexts) context.Dispose();
            _database.Dispose();
        }
    }

    private sealed record State(
        LeaveRequest Request,
        EmployeeLeaveBalance Balance,
        IReadOnlyList<LeaveBalanceTransaction> Ledger,
        IReadOnlyList<LeaveRequestEvent> Events);
}
