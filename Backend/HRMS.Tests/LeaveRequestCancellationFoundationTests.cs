using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class LeaveRequestCancellationFoundationTests
{
    [Fact]
    public async Task Owner_can_cancel_approved_request_and_preserve_history_and_immutable_snapshot()
    {
        using var fixture = await CancellationFixture.CreateAsync(LeaveRequestStatus.Approved, cancelAllowed: true);
        var before = await fixture.ReadRequestAsync();
        var result = await fixture.Service(fixture.OwnerEmployeeId).CancelAsync(fixture.RequestId);
        var after = await fixture.ReadRequestAsync();

        Assert.True(result.Succeeded);
        Assert.Equal(LeaveRequestStatus.Cancelled, after.Status);
        Assert.Equal(before.EmployeeId, after.EmployeeId);
        Assert.Equal(before.LeaveTypeId, after.LeaveTypeId);
        Assert.Equal(before.LeavePeriodId, after.LeavePeriodId);
        Assert.Equal(before.LeavePolicyVersionId, after.LeavePolicyVersionId);
        Assert.Equal(before.LeavePolicyRuleId, after.LeavePolicyRuleId);
        Assert.Equal(before.StartDate, after.StartDate);
        Assert.Equal(before.EndDate, after.EndDate);
        Assert.Equal(before.RequestedQuantity, after.RequestedQuantity);
        Assert.Equal(before.ChargeableQuantity, after.ChargeableQuantity);
        Assert.Equal(before.SubmittedAtUtc, after.SubmittedAtUtc);
        Assert.Single(after.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        Assert.Single(after.Events, x => x.EventType == LeaveRequestEventType.Approved);
        var cancelled = Assert.Single(after.Events, x => x.EventType == LeaveRequestEventType.Cancelled);
        Assert.Equal(fixture.OwnerUserId, cancelled.ActorUserId);
        Assert.Equal(fixture.OwnerEmployeeId, cancelled.ActorEmployeeId);
    }

    [Fact]
    public async Task Cancellation_uses_captured_rule_not_newer_policy_configuration()
    {
        using var fixture = await CancellationFixture.CreateAsync(LeaveRequestStatus.Approved, cancelAllowed: true, addNewerDisallowedRule: true);
        var result = await fixture.Service(fixture.OwnerEmployeeId).CancelAsync(fixture.RequestId);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task Captured_disallowed_rule_blocks_even_when_newer_rule_allows()
    {
        using var fixture = await CancellationFixture.CreateAsync(LeaveRequestStatus.Approved, cancelAllowed: false, addNewerAllowedRule: true);
        var result = await fixture.Service(fixture.OwnerEmployeeId).CancelAsync(fixture.RequestId);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("CancellationNotAllowed", result.Message);
        await fixture.AssertUnchangedAsync(LeaveRequestStatus.Approved);
    }

    [Fact]
    public async Task Other_employee_cross_tenant_and_unlinked_identities_receive_safe_failure()
    {
        using var fixture = await CancellationFixture.CreateAsync(LeaveRequestStatus.Approved, cancelAllowed: true);
        Assert.Equal(ResultStatus.NotFound, (await fixture.Service(fixture.OtherEmployeeId).CancelAsync(fixture.RequestId)).Status);
        Assert.Equal(ResultStatus.NotFound, (await fixture.ServiceWithIdentity(Result<RuntimeEmployeeIdentity>.Success(new(fixture.OtherTenantId, fixture.OtherUserId, fixture.OtherTenantEmployeeId))).CancelAsync(fixture.RequestId)).Status);
        Assert.Equal(ResultStatus.NotFound, (await fixture.ServiceWithIdentity(Result<RuntimeEmployeeIdentity>.NotFound("not linked")).CancelAsync(fixture.RequestId)).Status);
        await fixture.AssertUnchangedAsync(LeaveRequestStatus.Approved);
    }

    [Theory]
    [InlineData(LeaveRequestStatus.PendingApproval)]
    [InlineData(LeaveRequestStatus.Rejected)]
    [InlineData(LeaveRequestStatus.Withdrawn)]
    [InlineData(LeaveRequestStatus.Cancelled)]
    public async Task Non_approved_statuses_cannot_be_cancelled(LeaveRequestStatus status)
    {
        using var fixture = await CancellationFixture.CreateAsync(status, cancelAllowed: true);
        var result = await fixture.Service(fixture.OwnerEmployeeId).CancelAsync(fixture.RequestId);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("InvalidStatusTransition", result.Message);
        await fixture.AssertUnchangedAsync(status);
    }

    [Fact]
    public async Task Missing_or_disabled_cancellation_rule_is_restrictive()
    {
        using var disabled = await CancellationFixture.CreateAsync(LeaveRequestStatus.Approved, cancelAllowed: false);
        var disabledResult = await disabled.Service(disabled.OwnerEmployeeId).CancelAsync(disabled.RequestId);
        Assert.Contains("CancellationNotAllowed", disabledResult.Message);
        await disabled.AssertUnchangedAsync(LeaveRequestStatus.Approved);

        using var missing = await CancellationFixture.CreateAsync(LeaveRequestStatus.Approved, cancelAllowed: null);
        var missingResult = await missing.Service(missing.OwnerEmployeeId).CancelAsync(missing.RequestId);
        Assert.Contains("CancellationNotAllowed", missingResult.Message);
        await missing.AssertUnchangedAsync(LeaveRequestStatus.Approved);
    }

    [Theory]
    [InlineData(EntitlementMode.Unlimited)]
    [InlineData(EntitlementMode.NoBalanceRequired)]
    public async Task Supported_entitlement_modes_cancel_without_balance_mutation(EntitlementMode mode)
    {
        using var fixture = await CancellationFixture.CreateAsync(LeaveRequestStatus.Approved, cancelAllowed: true, entitlementMode: mode);
        var result = await fixture.Service(fixture.OwnerEmployeeId).CancelAsync(fixture.RequestId);
        Assert.True(result.Succeeded);
        Assert.Equal(LeaveRequestStatus.Cancelled, (await fixture.ReadRequestAsync()).Status);
        Assert.Equal(0, await fixture.ReadBalanceTransactionCountAsync());
    }

    [Fact]
    public async Task Allocated_cancellation_is_blocked_without_status_event_or_balance_mutation()
    {
        using var fixture = await CancellationFixture.CreateAsync(LeaveRequestStatus.Approved, cancelAllowed: true, entitlementMode: EntitlementMode.Allocated);
        var result = await fixture.Service(fixture.OwnerEmployeeId).CancelAsync(fixture.RequestId);
        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("AllocatedCancellationBalanceReleaseNotReady", result.Message);
        await fixture.AssertUnchangedAsync(LeaveRequestStatus.Approved);
        Assert.Equal(0, await fixture.ReadBalanceTransactionCountAsync());
    }

    [Fact]
    public async Task Cancellation_uses_employee_lock_and_retry_boundary_without_manager_resolution()
    {
        using var fixture = await CancellationFixture.CreateAsync(LeaveRequestStatus.Approved, cancelAllowed: true, withoutEmploymentHistory: true);
        var retry = new RecordingRetryPolicy();
        var result = await fixture.Service(fixture.OwnerEmployeeId, retry).CancelAsync(fixture.RequestId);
        Assert.True(result.Succeeded);
        Assert.Equal(1, retry.Attempts);
        Assert.Equal(fixture.OwnerEmployeeId, fixture.Lock.EmployeeId);
    }

    private sealed class StubIdentity(Result<RuntimeEmployeeIdentity> result) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class RecordingLock : ILeaveRequestSubmissionLock
    {
        public Guid EmployeeId { get; private set; }
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) { EmployeeId = employeeId; return Task.CompletedTask; }
    }

    private sealed class RecordingRetryPolicy : ILeaveRequestSubmissionRetryPolicy
    {
        public int Attempts { get; private set; }
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> attempt, CancellationToken cancellationToken = default) { Attempts++; return attempt(cancellationToken); }
    }

    private sealed class CancellationFixture : IDisposable
    {
        public readonly Guid TenantId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public readonly Guid OtherTenantId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public readonly Guid OwnerEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000001");
        public readonly Guid OtherEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000003");
        public readonly Guid OtherTenantEmployeeId = new("bbbbbbbb-0000-0000-0000-000000000001");
        public readonly Guid OwnerUserId = new("11111111-0000-0000-0000-000000000001");
        public readonly Guid OtherUserId = new("22222222-0000-0000-0000-000000000001");
        public readonly Guid RequestId = new("aaaaaaaa-1111-1111-1111-000000000001");
        private readonly SqliteInMemoryDatabase _database;
        private readonly List<HrmsDbContext> _contexts = [];
        public readonly RecordingLock Lock = new();
        private CancellationFixture(SqliteInMemoryDatabase database) => _database = database;

        public static async Task<CancellationFixture> CreateAsync(LeaveRequestStatus status, bool? cancelAllowed, EntitlementMode entitlementMode = EntitlementMode.Unlimited, bool addNewerDisallowedRule = false, bool addNewerAllowedRule = false, bool withoutEmploymentHistory = false)
        {
            var fixture = new CancellationFixture(new SqliteInMemoryDatabase());
            await fixture.SeedAsync(status, cancelAllowed, entitlementMode, addNewerDisallowedRule, addNewerAllowedRule, withoutEmploymentHistory);
            return fixture;
        }

        public LeaveRequestCancellationService Service(Guid employeeId, ILeaveRequestSubmissionRetryPolicy? retry = null) =>
            ServiceWithIdentity(Result<RuntimeEmployeeIdentity>.Success(new(TenantId, employeeId == OwnerEmployeeId ? OwnerUserId : OtherUserId, employeeId)), retry);
        public LeaveRequestCancellationService ServiceWithIdentity(Result<RuntimeEmployeeIdentity> identity, ILeaveRequestSubmissionRetryPolicy? retry = null)
        {
            var context = _database.CreateContext(new TestTenantContext(TenantId)); _contexts.Add(context);
            return new LeaveRequestCancellationService(context, new StubIdentity(identity), Lock, TimeProvider.System, retry);
        }
        public async Task<LeaveRequest> ReadRequestAsync()
        {
            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            return await context.LeaveRequests.Include(x => x.Events).Include(x => x.Days).SingleAsync(x => x.Id == RequestId);
        }
        public async Task<int> ReadBalanceTransactionCountAsync()
        {
            await using var context = _database.CreateContext(new TestTenantContext(TenantId)); return await context.LeaveBalanceTransactions.CountAsync();
        }
        public async Task AssertUnchangedAsync(LeaveRequestStatus expectedStatus)
        {
            var request = await ReadRequestAsync(); Assert.Equal(expectedStatus, request.Status); Assert.DoesNotContain(request.Events, x => x.EventType == LeaveRequestEventType.Cancelled);
        }

        private async Task SeedAsync(LeaveRequestStatus status, bool? cancelAllowed, EntitlementMode entitlementMode, bool addNewerDisallowedRule, bool addNewerAllowedRule, bool withoutEmploymentHistory)
        {
            await using (var unscoped = _database.CreateContext(new TestTenantContext()))
            {
                unscoped.Tenants.AddRange(new Tenant { Id = TenantId, TenantCode = "TESTA", Host = "testa.localhost", ShardKey = "testa", TenantName = "Test A" }, new Tenant { Id = OtherTenantId, TenantCode = "TESTB", Host = "testb.localhost", ShardKey = "testb", TenantName = "Test B" }); await unscoped.SaveChangesAsync();
            }
            var typeId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var policyId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var ruleId = Guid.NewGuid(); var historyId = Guid.NewGuid();
            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            var rule = new LeavePolicyRule { Id = ruleId, TenantId = TenantId, LeavePolicyVersionId = versionId, LeaveTypeId = typeId };
            context.AddRange(new User { Id = OwnerUserId, TenantId = TenantId, Email = "owner@test.local", PasswordHash = "test", FirstName = "Owner", LastName = "Employee" }, new Employee { Id = OwnerEmployeeId, TenantId = TenantId, EmployeeCode = "EMP-1", FirstName = "Owner", LastName = "Employee", Email = "owner-employee@test.local", DateOfJoining = new(2020, 1, 1) }, new Employee { Id = OtherEmployeeId, TenantId = TenantId, EmployeeCode = "EMP-2", FirstName = "Other", LastName = "Employee", Email = "other@test.local", DateOfJoining = new(2020, 1, 1) }, new LeaveType { Id = typeId, TenantId = TenantId, Code = "CL", Name = "Casual Leave" }, new LeavePeriod { Id = periodId, TenantId = TenantId, Code = "FY26", Name = "Financial Year 2026", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) }, new LeavePolicy { Id = policyId, TenantId = TenantId, Code = "DEFAULT", Name = "Default" }, new LeavePolicyVersion { Id = versionId, TenantId = TenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published }, rule, new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = ruleId, EntitlementMode = entitlementMode });
            if (cancelAllowed is not null) context.LeavePolicyCancellationRules.Add(new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = ruleId, CancelAllowed = cancelAllowed.Value });
            if (addNewerDisallowedRule || addNewerAllowedRule)
            {
                var newerVersionId = Guid.NewGuid();
                var newerRuleId = Guid.NewGuid();
                var newerAllowed = addNewerAllowedRule && !addNewerDisallowedRule;
                context.AddRange(
                    new LeavePolicyVersion { Id = newerVersionId, TenantId = TenantId, LeavePolicyId = policyId, VersionNumber = 2, EffectiveFrom = new(2027, 1, 1), Status = LeavePolicyVersionStatus.Published },
                    new LeavePolicyRule { Id = newerRuleId, TenantId = TenantId, LeavePolicyVersionId = newerVersionId, LeaveTypeId = typeId },
                    new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = newerRuleId, EntitlementMode = entitlementMode },
                    new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = newerRuleId, CancelAllowed = newerAllowed });
            }
            context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = historyId, TenantId = TenantId, EmployeeId = OwnerEmployeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active, ManagerId = withoutEmploymentHistory ? null : null });
            context.LeaveRequests.Add(new LeaveRequest { Id = RequestId, TenantId = TenantId, EmployeeId = OwnerEmployeeId, LeaveTypeId = typeId, LeavePeriodId = periodId, LeavePolicyVersionId = versionId, LeavePolicyRuleId = ruleId, EmployeeEmploymentHistoryId = historyId, StartDate = new(2026, 10, 5), EndDate = new(2026, 10, 5), RequestedQuantity = 1, ChargeableQuantity = 1, Status = status, SubmittedAtUtc = new(2026, 9, 1), IdempotencyKey = "cancel-test", PayloadFingerprint = new string('a', 64), Days = [new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = TenantId, Date = new(2026, 10, 5), RequestedQuantity = 1, ChargeableQuantity = 1 }] });
            await context.SaveChangesAsync();
            context.LeaveRequestEvents.AddRange(new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = RequestId, EventType = LeaveRequestEventType.Submitted, OccurredAtUtc = new(2026, 9, 1), ActorType = LeaveBalanceActorType.User, ActorUserId = OwnerUserId, ActorEmployeeId = OwnerEmployeeId }, new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = RequestId, EventType = LeaveRequestEventType.Approved, OccurredAtUtc = new(2026, 9, 2), ActorType = LeaveBalanceActorType.User, ActorUserId = OwnerUserId, ActorEmployeeId = OwnerEmployeeId });
            await context.SaveChangesAsync();
        }
        public void Dispose() { foreach (var context in _contexts) context.Dispose(); _database.Dispose(); }
    }
}
