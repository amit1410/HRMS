using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class LeaveRequestWithdrawalFoundationTests
{
    [Fact]
    public async Task Linked_owner_can_withdraw_pending_request_and_event_captures_owner()
    {
        using var fixture = await WithdrawalFixture.CreateAsync();

        var result = await fixture.Service(fixture.OwnerEmployeeId).WithdrawAsync(fixture.RequestId);

        Assert.True(result.Succeeded);
        Assert.Equal(LeaveRequestStatus.Withdrawn, result.Value!.Status);
        var request = await fixture.ReadRequestAsync();
        Assert.Equal(LeaveRequestStatus.Withdrawn, request.Status);
        Assert.Single(request.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        var withdrawn = Assert.Single(request.Events, x => x.EventType == LeaveRequestEventType.Withdrawn);
        Assert.Equal(fixture.OwnerUserId, withdrawn.ActorUserId);
        Assert.Equal(fixture.OwnerEmployeeId, withdrawn.ActorEmployeeId);
    }

    [Fact]
    public async Task Other_employee_manager_and_admin_cannot_withdraw_owner_request()
    {
        using var fixture = await WithdrawalFixture.CreateAsync();

        Assert.Equal(ResultStatus.NotFound, (await fixture.Service(fixture.OtherEmployeeId).WithdrawAsync(fixture.RequestId)).Status);
        Assert.Equal(ResultStatus.NotFound, (await fixture.Service(fixture.ManagerEmployeeId).WithdrawAsync(fixture.RequestId)).Status);
        Assert.Equal(ResultStatus.NotFound, (await fixture.Service(fixture.AdminEmployeeId).WithdrawAsync(fixture.RequestId)).Status);

        var request = await fixture.ReadRequestAsync();
        Assert.Equal(LeaveRequestStatus.PendingApproval, request.Status);
        Assert.Single(request.Events);
    }

    [Fact]
    public async Task Unlinked_and_cross_tenant_identities_cannot_withdraw()
    {
        using var fixture = await WithdrawalFixture.CreateAsync();

        var unlinked = fixture.ServiceWithIdentity(Result<RuntimeEmployeeIdentity>.NotFound("The authenticated account is not linked to an Employee."));
        Assert.Equal(ResultStatus.NotFound, (await unlinked.WithdrawAsync(fixture.RequestId)).Status);
        Assert.Equal(ResultStatus.NotFound, (await fixture.ServiceWithIdentity(
            Result<RuntimeEmployeeIdentity>.Success(new(fixture.OtherTenantId, fixture.OtherUserId, fixture.OtherTenantEmployeeId)))
            .WithdrawAsync(fixture.RequestId)).Status);
    }

    [Theory]
    [InlineData(LeaveRequestStatus.Approved)]
    [InlineData(LeaveRequestStatus.Rejected)]
    [InlineData(LeaveRequestStatus.Withdrawn)]
    [InlineData(LeaveRequestStatus.Cancelled)]
    public async Task Non_pending_status_cannot_be_withdrawn_and_writes_no_event(LeaveRequestStatus status)
    {
        using var fixture = await WithdrawalFixture.CreateAsync(status);

        var result = await fixture.Service(fixture.OwnerEmployeeId).WithdrawAsync(fixture.RequestId);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        var request = await fixture.ReadRequestAsync();
        Assert.Equal(status, request.Status);
        Assert.Single(request.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        Assert.DoesNotContain(request.Events, x => x.EventType == LeaveRequestEventType.Withdrawn);
    }

    [Fact]
    public async Task Owner_withdrawal_does_not_require_manager_resolution_and_uses_lock_and_retry()
    {
        using var fixture = await WithdrawalFixture.CreateAsync(withoutEmploymentHistory: true);
        var retry = new RecordingRetryPolicy();

        var result = await fixture.Service(fixture.OwnerEmployeeId, retry).WithdrawAsync(fixture.RequestId);

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
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default)
        {
            EmployeeId = employeeId;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRetryPolicy : ILeaveRequestSubmissionRetryPolicy
    {
        public int Attempts { get; private set; }
        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> attempt, CancellationToken cancellationToken = default)
        {
            Attempts++;
            return attempt(cancellationToken);
        }
    }

    private sealed class WithdrawalFixture : IDisposable
    {
        public readonly Guid TenantId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public readonly Guid OtherTenantId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public readonly Guid OwnerEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000001");
        public readonly Guid ManagerEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000002");
        public readonly Guid OtherEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000003");
        public readonly Guid AdminEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000004");
        public readonly Guid OtherTenantEmployeeId = new("bbbbbbbb-0000-0000-0000-000000000001");
        public readonly Guid OwnerUserId = new("11111111-0000-0000-0000-000000000001");
        public readonly Guid OtherUserId = new("22222222-0000-0000-0000-000000000001");
        public readonly Guid RequestId = new("aaaaaaaa-1111-1111-1111-000000000001");
        private readonly SqliteInMemoryDatabase _database;
        private readonly List<HrmsDbContext> _contexts = [];
        public readonly RecordingLock Lock = new();

        private WithdrawalFixture(SqliteInMemoryDatabase database) => _database = database;

        public static async Task<WithdrawalFixture> CreateAsync(
            LeaveRequestStatus status = LeaveRequestStatus.PendingApproval,
            bool withoutEmploymentHistory = false)
        {
            var fixture = new WithdrawalFixture(new SqliteInMemoryDatabase());
            await fixture.SeedAsync(status, withoutEmploymentHistory);
            return fixture;
        }

        public LeaveRequestWithdrawalService Service(Guid employeeId, ILeaveRequestSubmissionRetryPolicy? retry = null) =>
            ServiceWithIdentity(Result<RuntimeEmployeeIdentity>.Success(new(TenantId, employeeId == OwnerEmployeeId ? OwnerUserId : OtherUserId, employeeId)), retry);

        public LeaveRequestWithdrawalService ServiceWithIdentity(Result<RuntimeEmployeeIdentity> identity, ILeaveRequestSubmissionRetryPolicy? retry = null)
        {
            var context = _database.CreateContext(new TestTenantContext(TenantId));
            _contexts.Add(context);
            return new LeaveRequestWithdrawalService(context, new StubIdentity(identity), Lock, TimeProvider.System, retry);
        }

        public async Task<LeaveRequest> ReadRequestAsync()
        {
            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            return await context.LeaveRequests.Include(x => x.Events).SingleAsync(x => x.Id == RequestId);
        }

        private async Task SeedAsync(LeaveRequestStatus status, bool withoutEmploymentHistory)
        {
            await using (var unscoped = _database.CreateContext(new TestTenantContext()))
            {
                unscoped.Tenants.AddRange(
                    new Tenant { Id = TenantId, TenantCode = "TESTA", Host = "testa.localhost", ShardKey = "testa", TenantName = "Test A" },
                    new Tenant { Id = OtherTenantId, TenantCode = "TESTB", Host = "testb.localhost", ShardKey = "testb", TenantName = "Test B" });
                await unscoped.SaveChangesAsync();
            }

            var typeId = Guid.NewGuid();
            var periodId = Guid.NewGuid();
            var policyId = Guid.NewGuid();
            var versionId = Guid.NewGuid();
            var ruleId = Guid.NewGuid();
            var historyId = Guid.NewGuid();
            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            context.AddRange(
                new User { Id = OwnerUserId, TenantId = TenantId, Email = "owner@test.local", PasswordHash = "test", FirstName = "Owner", LastName = "Employee" },
                new Employee { Id = OwnerEmployeeId, TenantId = TenantId, EmployeeCode = "EMP-1", FirstName = "Owner", LastName = "Employee", Email = "owner-employee@test.local", DateOfJoining = new(2020, 1, 1) },
                new Employee { Id = ManagerEmployeeId, TenantId = TenantId, EmployeeCode = "MGR-1", FirstName = "Manager", LastName = "Employee", Email = "manager@test.local", DateOfJoining = new(2020, 1, 1) },
                new Employee { Id = OtherEmployeeId, TenantId = TenantId, EmployeeCode = "EMP-2", FirstName = "Other", LastName = "Employee", Email = "other@test.local", DateOfJoining = new(2020, 1, 1) },
                new Employee { Id = AdminEmployeeId, TenantId = TenantId, EmployeeCode = "ADM-1", FirstName = "Admin", LastName = "Employee", Email = "admin@test.local", DateOfJoining = new(2020, 1, 1) },
                new LeaveType { Id = typeId, TenantId = TenantId, Code = "CL", Name = "Casual Leave" },
                new LeavePeriod { Id = periodId, TenantId = TenantId, Code = "FY26", Name = "Financial Year 2026", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) },
                new LeavePolicy { Id = policyId, TenantId = TenantId, Code = "DEFAULT", Name = "Default" },
                new LeavePolicyVersion { Id = versionId, TenantId = TenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published },
                new LeavePolicyRule { Id = ruleId, TenantId = TenantId, LeavePolicyVersionId = versionId, LeaveTypeId = typeId },
                new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = ruleId, EntitlementMode = EntitlementMode.Unlimited });
            context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = historyId, TenantId = TenantId, EmployeeId = OwnerEmployeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active, ManagerId = withoutEmploymentHistory ? null : ManagerEmployeeId });
            context.LeaveRequests.Add(new LeaveRequest { Id = RequestId, TenantId = TenantId, EmployeeId = OwnerEmployeeId, LeaveTypeId = typeId, LeavePeriodId = periodId, LeavePolicyVersionId = versionId, LeavePolicyRuleId = ruleId, EmployeeEmploymentHistoryId = historyId, StartDate = new(2026, 10, 5), EndDate = new(2026, 10, 5), RequestedQuantity = 1, ChargeableQuantity = 1, Status = status, SubmittedAtUtc = new(2026, 9, 1), IdempotencyKey = "withdrawal-test", PayloadFingerprint = new string('a', 64) });
            await context.SaveChangesAsync();
            context.LeaveRequestEvents.Add(new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = RequestId, EventType = LeaveRequestEventType.Submitted, OccurredAtUtc = new(2026, 9, 1), ActorType = LeaveBalanceActorType.User });
            await context.SaveChangesAsync();
        }

        public void Dispose()
        {
            foreach (var context in _contexts) context.Dispose();
            _database.Dispose();
        }
    }
}
