using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Application.Services;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class CompOffLeaveIntegrationTests
{
    [Fact] public async Task Leave_submission_reserves_comp_off() { await using var f = await LeaveFixture.CreateAsync(); var result = await f.SubmitAsync(); var balance = await f.BalanceAsync(); Assert.True(result.Succeeded, result.Message); Assert.Equal(240, balance.AvailableMinutes); Assert.Equal(240, balance.ReservedMinutes); Assert.Equal(0, balance.ConsumedMinutes); Assert.Contains(await f.LedgerAsync(), x => x.EntryType == CompOffLedgerEntryType.Reserve); }
    [Fact] public async Task Leave_approval_consumes_reserved_comp_off() { await using var f = await LeaveFixture.CreateAsync(); await f.SubmitAsync(); var result = await f.ApproveAsync(); var balance = await f.BalanceAsync(); Assert.True(result.Succeeded, result.Message); Assert.Equal(240, balance.AvailableMinutes); Assert.Equal(0, balance.ReservedMinutes); Assert.Equal(240, balance.ConsumedMinutes); Assert.Contains(await f.LedgerAsync(), x => x.EntryType == CompOffLedgerEntryType.Consume); }
    [Fact] public async Task Leave_rejection_releases_reservation() { await using var f = await LeaveFixture.CreateAsync(); await f.SubmitAsync(); var result = await f.RejectAsync(); var balance = await f.BalanceAsync(); var ledger = await f.LedgerAsync(); Assert.True(result.Succeeded, result.Message); Assert.Equal(480, balance.AvailableMinutes); Assert.Equal(0, balance.ReservedMinutes); Assert.Equal(0, balance.ConsumedMinutes); Assert.Single(ledger, x => x.EntryType == CompOffLedgerEntryType.Release); }
    [Fact] public async Task Leave_cancellation_restores_comp_off() { await using var f = await LeaveFixture.CreateAsync(); await f.SubmitAsync(); await f.ApproveAsync(); var result = await f.CancelAsync(); var balance = await f.BalanceAsync(); var ledger = await f.LedgerAsync(); Assert.True(result.Succeeded, result.Message); Assert.Equal(480, balance.AvailableMinutes); Assert.Equal(0, balance.ConsumedMinutes); Assert.Single(ledger, x => x.EntryType == CompOffLedgerEntryType.Restore); }
    [Fact] public async Task Insufficient_comp_off_blocks_leave() { await using var f = await LeaveFixture.CreateAsync(creditedMinutes: 120); var result = await f.SubmitAsync(); var balance = await f.BalanceAsync(); Assert.False(result.Succeeded); Assert.Contains("InsufficientCompOffBalance", result.Message); Assert.Equal(120, balance.AvailableMinutes); Assert.Equal(0, balance.ReservedMinutes); Assert.DoesNotContain(await f.LedgerAsync(), x => x.EntryType == CompOffLedgerEntryType.Reserve); }
    [Fact] public async Task Expired_comp_off_cannot_be_consumed() { await using var f = await LeaveFixture.CreateAsync(expiryDays: 1); Assert.Equal(1, (await f.CompOff.ExpireAsync(f.WorkDate.AddDays(1))).Value); var result = await f.SubmitAsync(); Assert.False(result.Succeeded); Assert.Contains("InsufficientCompOffBalance", result.Message); }
    [Fact] public async Task Multiple_earnings_allocate_earliest_expiry_first() { await using var f = await LeaveFixture.CreateAsync(creditedMinutes: 480, expiryDays: 100); var later = await f.AddEarningAsync(f.WorkDate.AddDays(1), 480, 100); var result = await f.SubmitAsync(600); Assert.True(result.Succeeded, result.Message); var allocations = await f.AllocationsAsync(); Assert.Equal(f.FirstEarningId, allocations[0].EarningId); Assert.Equal(later, allocations[1].EarningId); }
    [Fact] public async Task Partial_day_leave_consumes_correct_units() { await using var f = await LeaveFixture.CreateAsync(); var result = await f.SubmitAsync(120); Assert.True(result.Succeeded, result.Message); var balance = await f.BalanceAsync(); Assert.Equal(120, balance.ReservedMinutes); }
    [Fact] public async Task Duplicate_leave_transition_is_idempotent() { await using var f = await LeaveFixture.CreateAsync(); await f.SubmitAsync(); Assert.True((await f.ApproveAsync()).Succeeded); var second = await f.ApproveAsync(); var balance = await f.BalanceAsync(); Assert.False(second.Succeeded); Assert.Equal(240, balance.ConsumedMinutes); Assert.Single(await f.LedgerAsync(), x => x.EntryType == CompOffLedgerEntryType.Consume); }
    [Fact] public async Task Existing_leave_approval_authorization_is_preserved() { await using var f = await LeaveFixture.CreateAsync(); await f.SubmitAsync(); var result = await f.ApproveAsEmployeeAsync(); Assert.False(result.Succeeded); Assert.Contains("cannot approve", result.Message, StringComparison.OrdinalIgnoreCase); Assert.Equal(240, (await f.BalanceAsync()).ReservedMinutes); }

    private sealed class FixedIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, employeeId)));
    }

    private sealed class FixedValidation(LeaveRequestValidationResult value) : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput input, CancellationToken cancellationToken = default) => Task.FromResult(Result<LeaveRequestValidationResult>.Success(value));
    }

    private sealed class NoOpLock : IEmployeeSerializationLock
    {
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public sealed class LeaveFixture : IAsyncDisposable
    {
        private readonly SqliteInMemoryDatabase database;
        private readonly HrmsDbContext db;
        private readonly Guid managerUserId = Guid.NewGuid();
        private readonly Guid managerEmployeeId = Guid.NewGuid();
        private readonly Guid employeeUserId = Guid.NewGuid();
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly Guid EmployeeId = Guid.NewGuid();
        public readonly Guid LeaveTypeId = Guid.NewGuid();
        public readonly Guid LeavePeriodId = Guid.NewGuid();
        public readonly Guid PolicyVersionId = Guid.NewGuid();
        public readonly Guid PolicyRuleId = Guid.NewGuid();
        public readonly Guid EmploymentId = Guid.NewGuid();
        public readonly DateOnly WorkDate = new(2026, 9, 15);
        public Guid FirstEarningId { get; private set; }
        public ICompOffService CompOff { get; }

        private LeaveFixture(SqliteInMemoryDatabase database, HrmsDbContext db, Guid tenantId) { this.database = database; this.db = db; TenantId = tenantId; CompOff = new CompOffService(db, new TestTenantContext(TenantId), TimeProvider.System); }

        public static async Task<LeaveFixture> CreateAsync(int creditedMinutes = 480, int? expiryDays = null)
        {
            var database = new SqliteInMemoryDatabase();
            var tenantId = Guid.NewGuid();
            await using (var catalog = database.CreateCatalogContext()) { catalog.Tenants.Add(new Tenant { Id = tenantId, TenantCode = "COLIFE", TenantName = "Comp-Off Leave", Host = "colife.test", ShardKey = tenantId.ToString("N") }); await catalog.SaveChangesAsync(); }
            var db = database.CreateContext(new TestTenantContext(tenantId));
            var fixture = new LeaveFixture(database, db, tenantId);
            await fixture.SeedAsync(creditedMinutes, expiryDays);
            return fixture;
        }

        private async Task SeedAsync(int creditedMinutes, int? expiryDays)
        {
            db.Tenants.Add(new Tenant { Id = TenantId, TenantCode = $"CO{TenantId:N}"[..12], TenantName = "Comp-Off Leave", Host = $"{TenantId:N}.test", ShardKey = TenantId.ToString("N") });
            db.Users.AddRange(new User { Id = employeeUserId, TenantId = TenantId, Email = $"employee-{EmployeeId:N}@test.local", PasswordHash = "test", FirstName = "Employee", LastName = "User" }, new User { Id = managerUserId, TenantId = TenantId, Email = $"manager-{managerEmployeeId:N}@test.local", PasswordHash = "test", FirstName = "Manager", LastName = "User" });
            db.Employees.AddRange(new Employee { Id = EmployeeId, TenantId = TenantId, EmployeeCode = "CO-EMP", FirstName = "Employee", LastName = "User", Email = $"employee-{EmployeeId:N}@test.local", DateOfJoining = new(2026, 1, 1), ReportingManagerId = managerEmployeeId }, new Employee { Id = managerEmployeeId, TenantId = TenantId, EmployeeCode = "CO-MGR", FirstName = "Manager", LastName = "User", Email = $"manager-{managerEmployeeId:N}@test.local", DateOfJoining = new(2026, 1, 1) });
            db.EmployeeEmploymentHistory.AddRange(new EmployeeEmploymentHistory { Id = EmploymentId, TenantId = TenantId, EmployeeId = EmployeeId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active, ManagerId = managerEmployeeId }, new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = managerEmployeeId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            db.Permissions.Add(new Permission { Id = 35, Name = Permissions.Leave.Approve }); db.Roles.Add(new Role { Id = 900, Name = "CompOffLeaveApprover" }); db.UserRoles.Add(new UserRole { UserId = managerUserId, RoleId = 900, TenantId = TenantId }); db.RolePermissions.Add(new RolePermission { RoleId = 900, PermissionId = 35 });
            db.LeaveTypes.Add(new LeaveType { Id = LeaveTypeId, TenantId = TenantId, Code = "CO", Name = "Comp-Off", IsCompOff = true });
            db.LeavePeriods.Add(new LeavePeriod { Id = LeavePeriodId, TenantId = TenantId, Code = "2026", Name = "2026", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) });
            var policyId = Guid.NewGuid(); var ruleId = PolicyRuleId;
            db.LeavePolicies.Add(new LeavePolicy { Id = policyId, TenantId = TenantId, Code = "CO-POL", Name = "Comp-Off Leave" }); db.LeavePolicyVersions.Add(new LeavePolicyVersion { Id = PolicyVersionId, TenantId = TenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published }); db.LeavePolicyRules.Add(new LeavePolicyRule { Id = ruleId, TenantId = TenantId, LeavePolicyVersionId = PolicyVersionId, LeaveTypeId = LeaveTypeId }); db.LeavePolicyEntitlementRules.Add(new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = ruleId, EntitlementMode = EntitlementMode.Allocated }); db.LeavePolicyCancellationRules.Add(new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = ruleId, CancelAllowed = true });
            var day = new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, BusinessDate = WorkDate, RosterDayType = RosterDayType.WeeklyOff, Status = EmployeeAttendanceDayStatus.WeeklyOff, ExpectedWorkMinutes = 480, WorkedMinutes = creditedMinutes, ProcessedAtUtc = DateTime.UtcNow }; db.EmployeeAttendanceDays.Add(day); await db.SaveChangesAsync();
            var policy = await CompOff.CreatePolicyAsync(new("CO-EARN", "Comp-Off Earning", new(2026, 1, 1), null, true, true, true, 0, 1, CompOffRoundingMode.None, 0, null, null, expiryDays, null, false, true, 1, CompOffBenefitMode.CompOffOnly, "All")); Assert.True(policy.Succeeded, policy.Message); var earning = await CompOff.EarnAsync(new(EmployeeId, WorkDate, CompOffSourceType.WeekOff, day.Id, 1)); Assert.True(earning.Succeeded, earning.Message); FirstEarningId = earning.Value!.Id;
        }

        public async Task<Guid> AddEarningAsync(DateOnly date, int minutes, int expiryDays) { var day = new EmployeeAttendanceDay { Id = Guid.NewGuid(), TenantId = TenantId, EmployeeId = EmployeeId, BusinessDate = date, RosterDayType = RosterDayType.WeeklyOff, Status = EmployeeAttendanceDayStatus.WeeklyOff, ExpectedWorkMinutes = 480, WorkedMinutes = minutes, ProcessedAtUtc = DateTime.UtcNow }; db.EmployeeAttendanceDays.Add(day); await db.SaveChangesAsync(); var result = await CompOff.EarnAsync(new(EmployeeId, date, CompOffSourceType.WeekOff, day.Id, 1)); Assert.True(result.Succeeded, result.Message); return result.Value!.Id; }
        public Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(int minutes = 240) => SubmitCoreAsync(minutes);
        private async Task<Result<LeaveRequestSubmissionResult>> SubmitCoreAsync(int minutes) { var context = database.CreateContext(new TestTenantContext(TenantId)); var validation = new FixedValidation(new(EmployeeId, LeaveTypeId, EmploymentId, LeavePeriodId, PolicyVersionId, PolicyRuleId, Gender.Unspecified, WorkDate.AddDays(1), WorkDate.AddDays(1), minutes / 480m, minutes / 480m, [new(WorkDate.AddDays(1), minutes / 480m, minutes / 480m, "WorkingDay", "Comp-Off", true)], EntitlementMode.Allocated, true, false, Guid.NewGuid().ToString("N"), new string('c', 64), 1, 1)); var service = new LeaveRequestSubmissionService(context, new FixedIdentity(TenantId, employeeUserId, EmployeeId), validation, new NoOpLock(), TimeProvider.System, compOffService: new CompOffService(context, new TestTenantContext(TenantId), TimeProvider.System)); var result = await service.SubmitAsync(new(LeaveTypeId, WorkDate.AddDays(1), WorkDate.AddDays(1), Guid.NewGuid().ToString("N"))); context.Dispose(); return result; }
        public Task<Result<LeaveRequestApprovalResult>> ApproveAsync() => TransitionAsync(true, managerUserId, managerEmployeeId);
        public Task<Result<LeaveRequestApprovalResult>> RejectAsync() => TransitionAsync(false, managerUserId, managerEmployeeId);
        public Task<Result<LeaveRequestApprovalResult>> ApproveAsEmployeeAsync() => TransitionAsync(true, employeeUserId, EmployeeId);
        private async Task<Result<LeaveRequestApprovalResult>> TransitionAsync(bool approve, Guid userId, Guid employeeId) { var requestId = await db.LeaveRequests.OrderByDescending(x => x.SubmittedAtUtc).Select(x => x.Id).FirstAsync(); var context = database.CreateContext(new TestTenantContext(TenantId)); var service = new LeaveRequestApprovalService(context, new FixedIdentity(TenantId, userId, employeeId), new EmployeeManagerResolver(context, new TestTenantContext(TenantId)), new NoOpLock(), TimeProvider.System, compOffService: new CompOffService(context, new TestTenantContext(TenantId), TimeProvider.System)); var result = approve ? await service.ApproveAsync(requestId) : await service.RejectAsync(requestId); context.Dispose(); return result; }
        public async Task<Result<LeaveRequestCancellationResult>> CancelAsync() { var requestId = await db.LeaveRequests.OrderByDescending(x => x.SubmittedAtUtc).Select(x => x.Id).FirstAsync(); var context = database.CreateContext(new TestTenantContext(TenantId)); var service = new LeaveRequestCancellationService(context, new FixedIdentity(TenantId, employeeUserId, EmployeeId), new NoOpLock(), TimeProvider.System, compOffService: new CompOffService(context, new TestTenantContext(TenantId), TimeProvider.System)); var result = await service.CancelAsync(requestId); context.Dispose(); return result; }
        public async Task<CompOffBalanceDto> BalanceAsync() => (await CompOff.GetBalanceAsync(EmployeeId)).Value!;
        public async Task<IReadOnlyList<CompOffLedgerDto>> LedgerAsync() => (await CompOff.GetLedgerAsync(EmployeeId)).Value!;
        public Task<List<CompOffLeaveAllocation>> AllocationsAsync() => db.CompOffLeaveAllocations.Include(x => x.Earning).OrderBy(x => x.Earning!.ExpiresOn).ThenBy(x => x.Earning!.SourceWorkDate).ThenBy(x => x.EarningId).ToListAsync();
        public ValueTask DisposeAsync() { db.Dispose(); database.Dispose(); return ValueTask.CompletedTask; }
    }
}
