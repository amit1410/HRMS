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

public sealed class LeaveRequestApprovalFoundationTests
{
    [Fact]
    public async Task Current_manager_with_permission_can_approve_and_event_captures_actor()
    {
        using var fixture = await ApprovalFixture.CreateAsync();
        var result = await fixture.Service(fixture.ManagerEmployeeId).ApproveAsync(fixture.RequestId);

        Assert.True(result.Succeeded);
        Assert.Equal(LeaveRequestStatus.Approved, result.Value!.Status);
        var request = await fixture.ReadRequestAsync();
        Assert.Equal(LeaveRequestStatus.Approved, request.Status);
        Assert.Contains(request.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        var approved = Assert.Single(request.Events, x => x.EventType == LeaveRequestEventType.Approved);
        Assert.Equal(fixture.ManagerUserId, approved.ActorUserId);
        Assert.Equal(fixture.ManagerEmployeeId, approved.ActorEmployeeId);
    }

    [Fact]
    public async Task Current_manager_with_permission_can_reject_and_preserves_submitted_event()
    {
        using var fixture = await ApprovalFixture.CreateAsync();
        var result = await fixture.Service(fixture.ManagerEmployeeId).RejectAsync(fixture.RequestId);

        Assert.True(result.Succeeded);
        var request = await fixture.ReadRequestAsync();
        Assert.Equal(LeaveRequestStatus.Rejected, request.Status);
        Assert.Single(request.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        Assert.Single(request.Events, x => x.EventType == LeaveRequestEventType.Rejected);
    }

    [Fact]
    public async Task Missing_permission_non_manager_and_self_approval_are_forbidden_without_events()
    {
        using var noPermission = await ApprovalFixture.CreateAsync(grantPermission: false);
        var noPermissionResult = await noPermission.Service(noPermission.ManagerEmployeeId).ApproveAsync(noPermission.RequestId);
        Assert.Equal(ResultStatus.Forbidden, noPermissionResult.Status);

        using var nonManager = await ApprovalFixture.CreateAsync();
        var nonManagerResult = await nonManager.Service(nonManager.OtherEmployeeId).ApproveAsync(nonManager.RequestId);
        Assert.Equal(ResultStatus.Forbidden, nonManagerResult.Status);

        using var self = await ApprovalFixture.CreateAsync();
        var selfResult = await self.Service(self.RequesterEmployeeId).ApproveAsync(self.RequestId);
        Assert.Equal(ResultStatus.Forbidden, selfResult.Status);

        Assert.Equal(1, (await noPermission.ReadRequestAsync()).Events.Count);
        Assert.Equal(1, (await nonManager.ReadRequestAsync()).Events.Count);
        Assert.Equal(1, (await self.ReadRequestAsync()).Events.Count);
    }

    [Fact]
    public async Task Unlinked_approver_and_cross_tenant_request_are_not_authorized()
    {
        using var fixture = await ApprovalFixture.CreateAsync();
        var unlinked = fixture.ServiceWithIdentity(Result<RuntimeEmployeeIdentity>.NotFound("The authenticated account is not linked to an Employee."));
        Assert.Equal(ResultStatus.NotFound, (await unlinked.ApproveAsync(fixture.RequestId)).Status);

        var otherTenant = await fixture.Service(fixture.ManagerEmployeeId).ApproveAsync(fixture.OtherTenantRequestId);
        Assert.Equal(ResultStatus.NotFound, otherTenant.Status);
    }

    [Fact]
    public async Task Manager_change_moves_authority_to_new_current_manager()
    {
        using var fixture = await ApprovalFixture.CreateAsync();
        fixture.ManagerResolver.ManagerId = fixture.NewManagerEmployeeId;

        Assert.Equal(ResultStatus.Forbidden, (await fixture.Service(fixture.ManagerEmployeeId).ApproveAsync(fixture.RequestId)).Status);
        Assert.True((await fixture.Service(fixture.NewManagerEmployeeId).ApproveAsync(fixture.RequestId)).Succeeded);
    }

    [Fact]
    public async Task Terminal_status_cannot_transition_again_and_writes_no_second_event()
    {
        using var fixture = await ApprovalFixture.CreateAsync();
        Assert.True((await fixture.Service(fixture.ManagerEmployeeId).ApproveAsync(fixture.RequestId)).Succeeded);
        var second = await fixture.Service(fixture.ManagerEmployeeId).RejectAsync(fixture.RequestId);

        Assert.Equal(ResultStatus.Conflict, second.Status);
        var request = await fixture.ReadRequestAsync();
        Assert.Equal(LeaveRequestStatus.Approved, request.Status);
        Assert.Single(request.Events, x => x.EventType == LeaveRequestEventType.Approved);
    }

    [Fact]
    public async Task Lock_and_whole_attempt_retry_are_used()
    {
        using var fixture = await ApprovalFixture.CreateAsync();
        var retry = new RecordingRetryPolicy();
        var service = fixture.Service(fixture.ManagerEmployeeId, retry);

        Assert.True((await service.ApproveAsync(fixture.RequestId)).Succeeded);
        Assert.Equal(1, retry.Attempts);
        Assert.Equal(fixture.RequesterEmployeeId, fixture.Lock.EmployeeId);
    }

    private sealed class StubIdentity(Result<RuntimeEmployeeIdentity> result) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class StubManagerResolver(Guid managerId) : IEmployeeManagerResolver
    {
        public Guid ManagerId { get; set; } = managerId;
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.Resolved, employeeId, ManagerId, "MGR", "Manager", "resolved")));
        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(false);
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

    private sealed class ApprovalFixture : IDisposable
    {
        public readonly Guid TenantId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public readonly Guid OtherTenantId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public readonly Guid RequesterEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000001");
        public readonly Guid ManagerEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000002");
        public readonly Guid NewManagerEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000003");
        public readonly Guid OtherEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000004");
        public readonly Guid ManagerUserId = new("11111111-0000-0000-0000-000000000001");
        public readonly Guid NewManagerUserId = new("11111111-0000-0000-0000-000000000002");
        public readonly Guid RequestId = new("aaaaaaaa-1111-1111-1111-000000000001");
        public readonly Guid OtherTenantRequestId = new("bbbbbbbb-1111-1111-1111-000000000001");
        private readonly SqliteInMemoryDatabase _database;
        private readonly List<HrmsDbContext> _contexts = [];
        public readonly StubManagerResolver ManagerResolver;
        public readonly RecordingLock Lock = new();

        private ApprovalFixture(SqliteInMemoryDatabase database, StubManagerResolver managerResolver) { _database = database; ManagerResolver = managerResolver; }

        public static async Task<ApprovalFixture> CreateAsync(bool grantPermission = true)
        {
            var database = new SqliteInMemoryDatabase();
            var fixture = new ApprovalFixture(database, new StubManagerResolver(new("aaaaaaaa-0000-0000-0000-000000000002")));
            await fixture.SeedAsync(grantPermission);
            return fixture;
        }

        public LeaveRequestApprovalService Service(Guid approverEmployeeId, ILeaveRequestSubmissionRetryPolicy? retry = null) =>
            ServiceWithIdentity(Result<RuntimeEmployeeIdentity>.Success(new(TenantId, approverEmployeeId == NewManagerEmployeeId ? NewManagerUserId : ManagerUserId, approverEmployeeId)), retry);

        public LeaveRequestApprovalService ServiceWithIdentity(Result<RuntimeEmployeeIdentity> identity, ILeaveRequestSubmissionRetryPolicy? retry = null)
        {
            var context = _database.CreateContext(new TestTenantContext(TenantId));
            _contexts.Add(context);
            return new LeaveRequestApprovalService(context, new StubIdentity(identity), ManagerResolver, Lock, TimeProvider.System, retry);
        }

        public async Task<LeaveRequest> ReadRequestAsync()
        {
            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            return await context.LeaveRequests.Include(x => x.Events).SingleAsync(x => x.Id == RequestId);
        }

        private async Task SeedAsync(bool grantPermission)
        {
            await using (var unscoped = _database.CreateContext(new TestTenantContext()))
            {
                unscoped.Tenants.AddRange(
                    new Tenant { Id = TenantId, TenantCode = "TESTA", Host = "testa.localhost", ShardKey = "testa", TenantName = "Test A" },
                    new Tenant { Id = OtherTenantId, TenantCode = "TESTB", Host = "testb.localhost", ShardKey = "testb", TenantName = "Test B" });
                await unscoped.SaveChangesAsync();
            }

            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            var typeId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var policyId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var ruleId = Guid.NewGuid();
            var historyId = Guid.NewGuid(); var managerHistoryId = Guid.NewGuid();
            context.AddRange(
                new Role { Id = 900, Name = "ApprovalTestRole" },
                new Permission { Id = 35, Name = Permissions.Leave.Approve },
                new User { Id = ManagerUserId, TenantId = TenantId, Email = "manager@test.local", PasswordHash = "test", FirstName = "Current", LastName = "Manager" },
                new User { Id = NewManagerUserId, TenantId = TenantId, Email = "new-manager@test.local", PasswordHash = "test", FirstName = "New", LastName = "Manager" },
                new UserRole { UserId = ManagerUserId, RoleId = 900, TenantId = TenantId },
                new UserRole { UserId = NewManagerUserId, RoleId = 900, TenantId = TenantId },
                new Employee { Id = RequesterEmployeeId, TenantId = TenantId, EmployeeCode = "EMP-1", FirstName = "Request", LastName = "Employee", Email = "requester@test.local", DateOfJoining = new(2020, 1, 1) },
                new Employee { Id = ManagerEmployeeId, TenantId = TenantId, EmployeeCode = "MGR-1", FirstName = "Current", LastName = "Manager", Email = "manager-employee@test.local", DateOfJoining = new(2020, 1, 1) },
                new Employee { Id = NewManagerEmployeeId, TenantId = TenantId, EmployeeCode = "MGR-2", FirstName = "New", LastName = "Manager", Email = "new-manager-employee@test.local", DateOfJoining = new(2020, 1, 1) },
                new Employee { Id = OtherEmployeeId, TenantId = TenantId, EmployeeCode = "EMP-2", FirstName = "Other", LastName = "Employee", Email = "other@test.local", DateOfJoining = new(2020, 1, 1) },
                new EmployeeEmploymentHistory { Id = historyId, TenantId = TenantId, EmployeeId = RequesterEmployeeId, EffectiveFrom = new(2020, 1, 1), ManagerId = ManagerEmployeeId },
                new EmployeeEmploymentHistory { Id = managerHistoryId, TenantId = TenantId, EmployeeId = ManagerEmployeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active },
                new LeaveType { Id = typeId, TenantId = TenantId, Code = "CL", Name = "Casual Leave" },
                new LeavePeriod { Id = periodId, TenantId = TenantId, Code = "FY26", Name = "Financial Year 2026", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) },
                new LeavePolicy { Id = policyId, TenantId = TenantId, Code = "DEFAULT", Name = "Default" },
                new LeavePolicyVersion { Id = versionId, TenantId = TenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published },
                new LeavePolicyRule { Id = ruleId, TenantId = TenantId, LeavePolicyVersionId = versionId, LeaveTypeId = typeId },
                new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = ruleId, EntitlementMode = EntitlementMode.Unlimited });
            if (grantPermission) context.RolePermissions.Add(new RolePermission { RoleId = 900, PermissionId = 35 });
            context.LeaveRequests.Add(new LeaveRequest { Id = RequestId, TenantId = TenantId, EmployeeId = RequesterEmployeeId, LeaveTypeId = typeId, LeavePeriodId = periodId, LeavePolicyVersionId = versionId, LeavePolicyRuleId = ruleId, EmployeeEmploymentHistoryId = historyId, StartDate = new(2026, 10, 5), EndDate = new(2026, 10, 5), RequestedQuantity = 1, ChargeableQuantity = 1, SubmittedAtUtc = new(2026, 9, 1), IdempotencyKey = "approval-test", PayloadFingerprint = new string('a', 64) });
            await context.SaveChangesAsync();
            context.LeaveRequestEvents.Add(new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = RequestId, EventType = LeaveRequestEventType.Submitted, OccurredAtUtc = new(2026, 9, 1), ActorType = LeaveBalanceActorType.User });
            await context.SaveChangesAsync();

            var otherEmployeeId = new Guid("bbbbbbbb-0000-0000-0000-000000000001");
            var otherTypeId = new Guid("bbbbbbbb-0000-0000-0000-000000000011");
            var otherPeriodId = new Guid("bbbbbbbb-0000-0000-0000-000000000012");
            var otherPolicyId = new Guid("bbbbbbbb-0000-0000-0000-000000000013");
            var otherVersionId = new Guid("bbbbbbbb-0000-0000-0000-000000000014");
            var otherRuleId = new Guid("bbbbbbbb-0000-0000-0000-000000000015");
            var otherHistoryId = new Guid("bbbbbbbb-0000-0000-0000-000000000016");

            await using var otherTenantContext = _database.CreateContext(new TestTenantContext(OtherTenantId));
            otherTenantContext.AddRange(
                new Employee { Id = otherEmployeeId, TenantId = OtherTenantId, EmployeeCode = "EMP-B1", FirstName = "Other", LastName = "Tenant", Email = "other-tenant@test.local", DateOfJoining = new(2020, 1, 1) },
                new EmployeeEmploymentHistory { Id = otherHistoryId, TenantId = OtherTenantId, EmployeeId = otherEmployeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active },
                new LeaveType { Id = otherTypeId, TenantId = OtherTenantId, Code = "OT", Name = "Other Tenant Leave" },
                new LeavePeriod { Id = otherPeriodId, TenantId = OtherTenantId, Code = "FY26-B", Name = "Other Tenant Financial Year", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) },
                new LeavePolicy { Id = otherPolicyId, TenantId = OtherTenantId, Code = "OTHER", Name = "Other Tenant Policy" },
                new LeavePolicyVersion { Id = otherVersionId, TenantId = OtherTenantId, LeavePolicyId = otherPolicyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published },
                new LeavePolicyRule { Id = otherRuleId, TenantId = OtherTenantId, LeavePolicyVersionId = otherVersionId, LeaveTypeId = otherTypeId },
                new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = OtherTenantId, LeavePolicyRuleId = otherRuleId, EntitlementMode = EntitlementMode.Unlimited },
                new LeaveRequest { Id = OtherTenantRequestId, TenantId = OtherTenantId, EmployeeId = otherEmployeeId, LeaveTypeId = otherTypeId, LeavePeriodId = otherPeriodId, LeavePolicyVersionId = otherVersionId, LeavePolicyRuleId = otherRuleId, EmployeeEmploymentHistoryId = otherHistoryId, StartDate = new(2026, 10, 6), EndDate = new(2026, 10, 6), RequestedQuantity = 1, ChargeableQuantity = 1, SubmittedAtUtc = new(2026, 9, 2), IdempotencyKey = "other-tenant-approval-test", PayloadFingerprint = new string('b', 64) });
            await otherTenantContext.SaveChangesAsync();
        }

        public void Dispose() { foreach (var context in _contexts) context.Dispose(); _database.Dispose(); }
    }
}
