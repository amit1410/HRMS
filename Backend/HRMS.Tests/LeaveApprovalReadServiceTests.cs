using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class LeaveApprovalReadServiceTests
{
    [Fact]
    public async Task Inbox_returns_only_pending_requests_for_current_manager_in_newest_order_and_pages()
    {
        using var fixture = await ApprovalReadFixture.CreateAsync();
        var result = await fixture.ServiceFor(fixture.ManagerEmployeeId).GetInboxAsync(1, 2);

        Assert.True(result.Succeeded);
        Assert.Equal(3, result.Value!.TotalCount);
        Assert.Equal([fixture.RequestPendingNewest, fixture.RequestPendingMiddle], result.Value.Items.Select(x => x.RequestId));
        Assert.All(result.Value.Items, x => Assert.Equal(LeaveRequestStatus.PendingApproval, x.Status));
        Assert.DoesNotContain(result.Value.Items, x => x.RequestId == fixture.RequestApproved);
        Assert.DoesNotContain(result.Value.Items, x => x.RequestId == fixture.RequestRejected);
        Assert.DoesNotContain(result.Value.Items, x => x.EmployeeId == fixture.OtherEmployeeId);
    }

    [Fact]
    public async Task Inbox_empty_page_and_unlinked_or_unauthorized_accounts_are_safe()
    {
        using var fixture = await ApprovalReadFixture.CreateAsync();
        var empty = await fixture.ServiceFor(fixture.EmptyEmployeeId).GetInboxAsync(1, 25);
        var unlinked = await fixture.ServiceWithIdentity(Result<RuntimeEmployeeIdentity>.NotFound("The authenticated account is not linked to an Employee.")).GetInboxAsync(1, 25);
        var noPermission = await fixture.ServiceWithIdentity(Result<RuntimeEmployeeIdentity>.Forbidden("ApproverNotAuthorized: permission required")).GetInboxAsync(1, 25);

        Assert.True(empty.Succeeded);
        Assert.Empty(empty.Value!.Items);
        Assert.Equal(ResultStatus.NotFound, unlinked.Status);
        Assert.Equal(ResultStatus.Forbidden, noPermission.Status);
    }

    [Fact]
    public async Task Detail_returns_authoritative_days_and_events_in_order()
    {
        using var fixture = await ApprovalReadFixture.CreateAsync();
        var result = await fixture.ServiceFor(fixture.ManagerEmployeeId).GetByIdAsync(fixture.RequestPendingNewest);

        Assert.True(result.Succeeded);
        Assert.Equal(fixture.EmployeeId, result.Value!.EmployeeId);
        Assert.Equal([new DateOnly(2026, 12, 10), new DateOnly(2026, 12, 11)], result.Value.RequestDays.Select(x => x.Date));
        Assert.Equal(1m, result.Value.RequestDays[0].ChargeableQuantity);
        Assert.Equal([LeaveRequestEventType.Created, LeaveRequestEventType.Submitted, LeaveRequestEventType.Approved], result.Value.Events.Select(x => x.EventType));
    }

    [Fact]
    public async Task Detail_isolation_excludes_nonmanaged_self_and_cross_tenant_requests()
    {
        using var fixture = await ApprovalReadFixture.CreateAsync();
        var service = fixture.ServiceFor(fixture.ManagerEmployeeId);

        var nonManaged = await service.GetByIdAsync(fixture.RequestOtherEmployee);
        var self = await fixture.ServiceFor(fixture.EmployeeId).GetByIdAsync(fixture.RequestPendingNewest);
        var otherTenant = await service.GetByIdAsync(fixture.RequestOtherTenant);

        Assert.Equal(ResultStatus.NotFound, nonManaged.Status);
        Assert.Equal(ResultStatus.NotFound, self.Status);
        Assert.Equal(ResultStatus.NotFound, otherTenant.Status);
    }

    [Fact]
    public async Task Current_manager_change_moves_inbox_visibility()
    {
        using var fixture = await ApprovalReadFixture.CreateAsync();
        fixture.ManagerResolver.ManagerId = fixture.NewManagerEmployeeId;

        var oldManager = await fixture.ServiceFor(fixture.ManagerEmployeeId).GetInboxAsync(1, 25);
        var newManager = await fixture.ServiceFor(fixture.NewManagerEmployeeId).GetInboxAsync(1, 25);

        Assert.Empty(oldManager.Value!.Items);
        Assert.Contains(newManager.Value!.Items, x => x.RequestId == fixture.RequestPendingNewest);
    }

    private sealed class ApprovalReadFixture : IDisposable
    {
        public readonly Guid TenantId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public readonly Guid OtherTenantId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        public readonly Guid EmployeeId = new("aaaaaaaa-0000-0000-0000-000000000001");
        public readonly Guid ManagerEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000002");
        public readonly Guid NewManagerEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000003");
        public readonly Guid OtherEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000004");
        public readonly Guid EmptyEmployeeId = new("aaaaaaaa-0000-0000-0000-000000000005");
        public readonly Guid ManagerUserId = new("11111111-0000-0000-0000-000000000001");
        public readonly Guid RequestPendingNewest = new("aaaaaaaa-1111-1111-1111-000000000001");
        public readonly Guid RequestPendingMiddle = new("aaaaaaaa-1111-1111-1111-000000000002");
        public readonly Guid RequestPendingOldest = new("aaaaaaaa-1111-1111-1111-000000000003");
        public readonly Guid RequestApproved = new("aaaaaaaa-1111-1111-1111-000000000004");
        public readonly Guid RequestRejected = new("aaaaaaaa-1111-1111-1111-000000000005");
        public readonly Guid RequestOtherEmployee = new("aaaaaaaa-2222-2222-2222-000000000001");
        public readonly Guid RequestOtherTenant = new("bbbbbbbb-2222-2222-2222-000000000001");
        private readonly SqliteInMemoryDatabase _database;
        public readonly StubManagerResolver ManagerResolver = new();

        private ApprovalReadFixture(SqliteInMemoryDatabase database) => _database = database;

        public static async Task<ApprovalReadFixture> CreateAsync()
        {
            var database = new SqliteInMemoryDatabase();
            var fixture = new ApprovalReadFixture(database);
            await fixture.SeedAsync();
            return fixture;
        }

        public LeaveApprovalReadService ServiceFor(Guid employeeId) =>
            new(_database.CreateContext(new TestTenantContext(TenantId)),
                new StubIdentity(Result<RuntimeEmployeeIdentity>.Success(new(TenantId, ManagerUserId, employeeId))),
                ManagerResolver,
                TimeProvider.System);

        public LeaveApprovalReadService ServiceWithIdentity(Result<RuntimeEmployeeIdentity> identity) =>
            new(_database.CreateContext(new TestTenantContext(TenantId)), new StubIdentity(identity), ManagerResolver, TimeProvider.System);

        private async Task SeedAsync()
        {
            await using var unscoped = _database.CreateContext(new TestTenantContext());
            unscoped.Tenants.AddRange(
                new Tenant { Id = TenantId, TenantCode = "READ-A", Host = "read-a.test", ShardKey = "read-a", TenantName = "Read A" },
                new Tenant { Id = OtherTenantId, TenantCode = "READ-B", Host = "read-b.test", ShardKey = "read-b", TenantName = "Read B" });
            await unscoped.SaveChangesAsync();

            await using var db = _database.CreateContext(new TestTenantContext(TenantId));
            var typeId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var policyId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var ruleId = Guid.NewGuid();
            var historyId = Guid.NewGuid(); var otherHistoryId = Guid.NewGuid();
            db.AddRange(
                new User { Id = ManagerUserId, TenantId = TenantId, Email = "manager@test", PasswordHash = "test", FirstName = "Current", LastName = "Manager" },
                new Role { Id = 901, Name = "Approval Read Role" },
                new Permission { Id = 35, Name = "Leave.Approve" },
                new UserRole { TenantId = TenantId, UserId = ManagerUserId, RoleId = 901 },
                new RolePermission { RoleId = 901, PermissionId = 35 },
                new Employee { Id = EmployeeId, TenantId = TenantId, EmployeeCode = "EMP-1", FirstName = "Request", LastName = "Employee", Email = "request@test", DateOfJoining = new(2020, 1, 1) },
                new Employee { Id = ManagerEmployeeId, TenantId = TenantId, EmployeeCode = "MGR-1", FirstName = "Current", LastName = "Manager", Email = "manager@test", DateOfJoining = new(2020, 1, 1) },
                new Employee { Id = NewManagerEmployeeId, TenantId = TenantId, EmployeeCode = "MGR-2", FirstName = "New", LastName = "Manager", Email = "new-manager@test", DateOfJoining = new(2020, 1, 1) },
                new Employee { Id = OtherEmployeeId, TenantId = TenantId, EmployeeCode = "EMP-2", FirstName = "Other", LastName = "Employee", Email = "other@test", DateOfJoining = new(2020, 1, 1) },
                new Employee { Id = EmptyEmployeeId, TenantId = TenantId, EmployeeCode = "EMP-3", FirstName = "Empty", LastName = "Employee", Email = "empty@test", DateOfJoining = new(2020, 1, 1) },
                new EmployeeEmploymentHistory { Id = historyId, TenantId = TenantId, EmployeeId = EmployeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active },
                new EmployeeEmploymentHistory { Id = otherHistoryId, TenantId = TenantId, EmployeeId = OtherEmployeeId, EffectiveFrom = new(2020, 1, 1), EmploymentStatus = EmployeeStatus.Active },
                new LeaveType { Id = typeId, TenantId = TenantId, Code = "CL", Name = "Casual Leave" },
                new LeavePeriod { Id = periodId, TenantId = TenantId, Code = "FY26", Name = "Financial Year 2026", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) },
                new LeavePolicy { Id = policyId, TenantId = TenantId, Code = "DEFAULT", Name = "Default" },
                new LeavePolicyVersion { Id = versionId, TenantId = TenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published },
                new LeavePolicyRule { Id = ruleId, TenantId = TenantId, LeavePolicyVersionId = versionId, LeaveTypeId = typeId });
            AddRequest(db, RequestPendingNewest, EmployeeId, historyId, typeId, periodId, versionId, ruleId, LeaveRequestStatus.PendingApproval, new(2026, 12, 10), true);
            AddRequest(db, RequestPendingMiddle, EmployeeId, historyId, typeId, periodId, versionId, ruleId, LeaveRequestStatus.PendingApproval, new(2026, 12, 9));
            AddRequest(db, RequestPendingOldest, EmployeeId, historyId, typeId, periodId, versionId, ruleId, LeaveRequestStatus.PendingApproval, new(2026, 12, 8));
            AddRequest(db, RequestApproved, EmployeeId, historyId, typeId, periodId, versionId, ruleId, LeaveRequestStatus.Approved, new(2026, 12, 7));
            AddRequest(db, RequestRejected, EmployeeId, historyId, typeId, periodId, versionId, ruleId, LeaveRequestStatus.Rejected, new(2026, 12, 6));
            AddRequest(db, RequestOtherEmployee, OtherEmployeeId, otherHistoryId, typeId, periodId, versionId, ruleId, LeaveRequestStatus.PendingApproval, new(2026, 12, 5));
            await db.SaveChangesAsync();
            ManagerResolver.SubjectEmployeeId = EmployeeId;
            ManagerResolver.ManagerId = ManagerEmployeeId;
        }

        private void AddRequest(HrmsDbContext db, Guid id, Guid employeeId, Guid historyId, Guid typeId, Guid periodId, Guid versionId, Guid ruleId, LeaveRequestStatus status, DateOnly date, bool detailed = false)
        {
            db.LeaveRequests.Add(new LeaveRequest { Id = id, TenantId = TenantId, EmployeeId = employeeId, LeaveTypeId = typeId, LeavePeriodId = periodId, LeavePolicyVersionId = versionId, LeavePolicyRuleId = ruleId, EmployeeEmploymentHistoryId = historyId, StartDate = date, EndDate = date.AddDays(1), RequestedQuantity = 2, ChargeableQuantity = 1.5m, Status = status, SubmittedAtUtc = date.ToDateTime(new(9, 0)), IdempotencyKey = id.ToString(), PayloadFingerprint = new string('a', 64), Days = detailed ? [new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = id, Date = date.AddDays(1), RequestedQuantity = 1, ChargeableQuantity = 0.5m }, new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = id, Date = date, RequestedQuantity = 1, ChargeableQuantity = 1m }] : [], Events = detailed ? [new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = id, EventType = LeaveRequestEventType.Created, OccurredAtUtc = date.ToDateTime(new(8, 0)), ActorType = LeaveBalanceActorType.System }, new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = id, EventType = LeaveRequestEventType.Submitted, OccurredAtUtc = date.ToDateTime(new(9, 0)), ActorType = LeaveBalanceActorType.User }, new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = TenantId, LeaveRequestId = id, EventType = LeaveRequestEventType.Approved, OccurredAtUtc = date.ToDateTime(new(10, 0)), ActorType = LeaveBalanceActorType.User }] : [] });
        }

        public void Dispose() => _database.Dispose();
    }

    private sealed class StubIdentity(Result<RuntimeEmployeeIdentity> result) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class StubManagerResolver : IEmployeeManagerResolver
    {
        public Guid SubjectEmployeeId { get; set; }
        public Guid ManagerId { get; set; }
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(employeeId == SubjectEmployeeId
                ? Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.Resolved, employeeId, ManagerId, "MGR", "Manager", "resolved"))
                : Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.NoAssignedManager, employeeId, null, null, null, "not managed")));
        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
