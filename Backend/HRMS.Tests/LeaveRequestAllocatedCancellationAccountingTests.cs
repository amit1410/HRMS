using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class LeaveRequestAllocatedCancellationAccountingTests
{
    [Fact]
    public async Task Cancellation_restores_consumption_and_preserves_history()
    {
        using var fixture = await CancellationFixture.CreateAsync();

        var result = await fixture.CancelAsync();
        var state = await fixture.ReadAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(LeaveRequestStatus.Cancelled, state.Request.Status);
        Assert.Equal(9m, state.Balance.GrantedQuantity);
        Assert.Equal(0m, state.Balance.ReservedQuantity);
        Assert.Equal(2m, state.Balance.ConsumedQuantity);
        Assert.Equal(7m, state.Balance.AvailableQuantity);
        var restore = Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.CancellationRestore);
        Assert.Equal(3m, restore.Quantity);
        Assert.Equal(fixture.RequestId, restore.LeaveRequestId);
        Assert.Equal(fixture.LeavePeriodId, restore.LeavePeriodId);
        Assert.Equal(fixture.PolicyVersionId, restore.LeavePolicyVersionId);
        Assert.Equal(fixture.PolicyRuleId, restore.LeavePolicyRuleId);
        Assert.Equal(fixture.OwnerUserId, restore.ActorUserId);
        Assert.Equal(fixture.OwnerEmployeeId, restore.ActorEmployeeId);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Approved);
        var cancelled = Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Cancelled);
        Assert.Equal(fixture.OwnerUserId, cancelled.ActorUserId);
        Assert.Equal(fixture.OwnerEmployeeId, cancelled.ActorEmployeeId);
    }

    [Fact]
    public async Task Cancellation_restores_exact_consumption()
    {
        using var fixture = await CancellationFixture.CreateAsync(consumed: 3m);

        var result = await fixture.CancelAsync();
        var state = await fixture.ReadAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(0m, state.Balance.ConsumedQuantity);
        Assert.Equal(9m, state.Balance.AvailableQuantity);
        Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.CancellationRestore);
    }

    [Fact]
    public async Task Cancellation_without_sufficient_consumption_preserves_approved_request()
    {
        using var fixture = await CancellationFixture.CreateAsync(consumed: 2m);

        var result = await fixture.CancelAsync();
        var state = await fixture.ReadAsync();

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("AllocatedConsumptionNotFound", result.Message);
        Assert.Equal(LeaveRequestStatus.Approved, state.Request.Status);
        Assert.Equal(2m, state.Balance.ConsumedQuantity);
        Assert.DoesNotContain(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.CancellationRestore);
        Assert.DoesNotContain(state.Events, x => x.EventType == LeaveRequestEventType.Cancelled);
    }

    [Fact]
    public async Task Cancellation_twice_restores_consumption_once()
    {
        using var fixture = await CancellationFixture.CreateAsync();

        var first = await fixture.CancelAsync();
        var second = await fixture.CancelAsync();
        var state = await fixture.ReadAsync();

        Assert.True(first.Succeeded, first.Message);
        Assert.Equal(ResultStatus.Conflict, second.Status);
        Assert.Contains("InvalidStatusTransition", second.Message);
        Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.CancellationRestore);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Cancelled);
        Assert.Equal(2m, state.Balance.ConsumedQuantity);
    }

    [Fact]
    public async Task Disallowed_cancellation_prevents_allocated_restoration()
    {
        using var fixture = await CancellationFixture.CreateAsync(cancelAllowed: false);

        var result = await fixture.CancelAsync();
        var state = await fixture.ReadAsync();

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("CancellationNotAllowed", result.Message);
        Assert.Equal(LeaveRequestStatus.Approved, state.Request.Status);
        Assert.Equal(5m, state.Balance.ConsumedQuantity);
        Assert.DoesNotContain(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.CancellationRestore);
        Assert.DoesNotContain(state.Events, x => x.EventType == LeaveRequestEventType.Cancelled);
    }

    [Theory]
    [InlineData(EntitlementMode.Unlimited)]
    [InlineData(EntitlementMode.NoBalanceRequired)]
    public async Task Non_allocated_cancellation_does_not_create_restoration(EntitlementMode mode)
    {
        using var fixture = await CancellationFixture.CreateAsync(mode: mode, includeBalance: false);

        var result = await fixture.CancelAsync();
        var state = await fixture.ReadAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(LeaveRequestStatus.Cancelled, state.Request.Status);
        Assert.Empty(state.Ledger);
    }

    private sealed class FixedIdentity(Result<RuntimeEmployeeIdentity> result) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class NoOpLock : ILeaveRequestSubmissionLock
    {
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CancellationFixture : IDisposable
    {
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly Guid OwnerUserId = Guid.NewGuid();
        public readonly Guid OwnerEmployeeId = Guid.NewGuid();
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

        private CancellationFixture(SqliteInMemoryDatabase database) => _database = database;

        public static async Task<CancellationFixture> CreateAsync(
            EntitlementMode mode = EntitlementMode.Allocated,
            decimal consumed = 5m,
            bool cancelAllowed = true,
            bool includeBalance = true)
        {
            var fixture = new CancellationFixture(new SqliteInMemoryDatabase());
            await fixture.SeedAsync(mode, consumed, cancelAllowed, includeBalance);
            return fixture;
        }

        public Task<Result<LeaveRequestCancellationResult>> CancelAsync() =>
            Service().CancelAsync(RequestId);

        private LeaveRequestCancellationService Service()
        {
            var context = _database.CreateContext(new TestTenantContext(TenantId));
            _contexts.Add(context);
            var tenantContext = new TestTenantContext(TenantId);
            return new LeaveRequestCancellationService(
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
                await context.EmployeeLeaveBalances.SingleOrDefaultAsync(x => x.Id == _balanceId),
                await context.LeaveBalanceTransactions.Where(x => x.LeaveRequestId == RequestId).ToListAsync(),
                await context.LeaveRequestEvents.Where(x => x.LeaveRequestId == RequestId).ToListAsync());
        }

        private async Task SeedAsync(EntitlementMode mode, decimal consumed, bool cancelAllowed, bool includeBalance)
        {
            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            context.AddRange(
                new Tenant { Id = TenantId, TenantCode = TenantId.ToString("N")[..8], Host = $"{TenantId}.local", ShardKey = TenantId.ToString("N"), TenantName = "Test" },
                new User { Id = OwnerUserId, TenantId = TenantId, Email = $"owner-{OwnerUserId}@test.local", PasswordHash = "test", FirstName = "Owner", LastName = "Employee" },
                new Employee { Id = OwnerEmployeeId, TenantId = TenantId, EmployeeCode = "EMP-1", FirstName = "Owner", LastName = "Employee", Email = $"owner-{OwnerEmployeeId}@test.local", DateOfJoining = new(2020, 1, 1) },
                new LeaveType { Id = LeaveTypeId, TenantId = TenantId, Code = "AL", Name = "Annual Leave" },
                new LeavePeriod { Id = LeavePeriodId, TenantId = TenantId, Code = "2027", Name = "2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) },
                new LeavePolicy { Id = PolicyId, TenantId = TenantId, Code = "POL", Name = "Policy" },
                new LeavePolicyVersion { Id = PolicyVersionId, TenantId = TenantId, LeavePolicyId = PolicyId, VersionNumber = 1, EffectiveFrom = new(2027, 1, 1), Status = LeavePolicyVersionStatus.Published },
                new LeavePolicyRule { Id = PolicyRuleId, TenantId = TenantId, LeavePolicyVersionId = PolicyVersionId, LeaveTypeId = LeaveTypeId },
                new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = PolicyRuleId, EntitlementMode = mode },
                new EmployeeEmploymentHistory { Id = EmploymentId, TenantId = TenantId, EmployeeId = OwnerEmployeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active },
                new LeaveRequest { Id = RequestId, TenantId = TenantId, EmployeeId = OwnerEmployeeId, LeaveTypeId = LeaveTypeId, LeavePeriodId = LeavePeriodId, LeavePolicyVersionId = PolicyVersionId, LeavePolicyRuleId = PolicyRuleId, EmployeeEmploymentHistoryId = EmploymentId, StartDate = new(2027, 1, 2), EndDate = new(2027, 1, 2), RequestedQuantity = 3, ChargeableQuantity = 3, Status = LeaveRequestStatus.Approved, SubmittedAtUtc = new(2026, 12, 1), IdempotencyKey = RequestId.ToString("N"), PayloadFingerprint = new string('a', 64) });
            if (cancelAllowed)
                context.LeavePolicyCancellationRules.Add(new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = PolicyRuleId, CancelAllowed = true });
            if (includeBalance)
                context.EmployeeLeaveBalances.Add(new EmployeeLeaveBalance { Id = _balanceId, TenantId = TenantId, EmployeeId = OwnerEmployeeId, LeaveTypeId = LeaveTypeId, LeavePeriodId = LeavePeriodId, GrantedQuantity = 9, ReservedQuantity = 0, ConsumedQuantity = consumed });
            await context.SaveChangesAsync();
            context.LeaveRequestEvents.AddRange(
                new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = RequestId, EventType = LeaveRequestEventType.Submitted, OccurredAtUtc = new(2026, 12, 1), ActorType = LeaveBalanceActorType.User, ActorUserId = OwnerUserId, ActorEmployeeId = OwnerEmployeeId },
                new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = RequestId, EventType = LeaveRequestEventType.Approved, OccurredAtUtc = new(2026, 12, 2), ActorType = LeaveBalanceActorType.User, ActorUserId = OwnerUserId, ActorEmployeeId = OwnerEmployeeId });
            if (mode == EntitlementMode.Allocated && includeBalance)
                context.LeaveBalanceTransactions.Add(new LeaveBalanceTransaction { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeLeaveBalanceId = _balanceId, EmployeeId = OwnerEmployeeId, LeaveTypeId = LeaveTypeId, LeavePeriodId = LeavePeriodId, LeaveRequestId = RequestId, TransactionType = LeaveBalanceTransactionType.Consumption, Quantity = 3, EffectiveDate = new(2027, 1, 2), OccurredAtUtc = new(2026, 12, 2), LeavePolicyVersionId = PolicyVersionId, LeavePolicyRuleId = PolicyRuleId, SourceType = LeaveBalanceSourceType.Policy, SourceReference = $"LeaveRequest:{RequestId:D}", ActorType = LeaveBalanceActorType.User, ActorUserId = OwnerUserId, ActorEmployeeId = OwnerEmployeeId, IdempotencyKey = $"seed:{RequestId:N}", PayloadFingerprint = new string('b', 64) });
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
        EmployeeLeaveBalance? Balance,
        IReadOnlyList<LeaveBalanceTransaction> Ledger,
        IReadOnlyList<LeaveRequestEvent> Events);
}
