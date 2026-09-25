using HRMS.Application.Services;
using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class AttendanceLeaveIntegrationTests
{
    [Fact]
    public async Task Approved_leave_without_punches_produces_on_leave_without_mutating_leave()
    {
        using var fixture = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        var leave = await AddLeaveAsync(fixture, date, LeaveRequestStatus.Approved);

        var result = await ProcessAsync(fixture, date);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.OnLeave, result.Value!.Status);
        Assert.False(result.Value.LeaveConflict);
        Assert.Equal(0, result.Value.PunchCount);
        Assert.Null(result.Value.FirstPunchAtUtc);
        Assert.Null(result.Value.LastPunchAtUtc);
        Assert.Null(result.Value.WorkedMinutes);

        await AssertLeaveUnchangedAsync(fixture, leave, LeaveRequestStatus.Approved, date);
    }

    [Fact]
    public async Task Leave_approval_removes_resolved_absence_from_active_exceptions()
    {
        using var fixture = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        var absent = await ProcessAsync(fixture, date);
        Assert.Equal(EmployeeAttendanceDayStatus.Absent, absent.Value!.Status);
        var leave = await AddLeaveAsync(fixture, date, LeaveRequestStatus.PendingApproval);
        var before = await new AttendanceOperationsService(fixture.Context, fixture.TenantContext)
            .GetOperationalExceptionsAsync(new() { FromDate = date, ToDate = date, ExceptionType = AttendanceExceptionType.Absent, Page = 1, PageSize = 10 });
        Assert.Single(before.Value!.Items);

        var managerId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        var roleId = 903;
        var permissionId = 3903;
        fixture.Context.Employees.Add(new Employee { Id = managerId, TenantId = fixture.TenantId, EmployeeCode = "MGR-LEAVE", FirstName = "Leave", LastName = "Manager", Email = $"{managerId:N}@example.test", DateOfJoining = new(2026, 1, 1) });
        fixture.Context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = managerId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
        var requesterHistory = await fixture.Context.EmployeeEmploymentHistory.SingleAsync(x => x.EmployeeId == fixture.EmployeeId);
        requesterHistory.ManagerId = managerId;
        fixture.Context.Users.Add(new User { Id = managerUserId, TenantId = fixture.TenantId, Email = $"{managerUserId:N}@example.test", PasswordHash = "test-only", FirstName = "Leave", LastName = "Manager", IsActive = true });
        fixture.Context.Roles.Add(new Role { Id = roleId, Name = "AttendanceLeaveApprovalTest", Description = "test" });
        fixture.Context.Permissions.Add(new Permission { Id = permissionId, Name = Permissions.Leave.Approve, Description = "test" });
        fixture.Context.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionId = permissionId });
        fixture.Context.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), TenantId = fixture.TenantId, UserId = managerUserId, RoleId = roleId, EffectiveFrom = new(2026, 1, 1) });
        await fixture.Context.SaveChangesAsync();

        var identity = new FixedLeaveIdentity(new(fixture.TenantId, managerUserId, managerId));
        var managerResolver = new EmployeeManagerResolver(fixture.Context, fixture.TenantContext);
        var serializer = new TestEmployeeSerializationLock();
        var attendanceProcessor = new AttendanceDayProcessor(fixture.Context, fixture.TenantContext, fixture.CalendarService,
            new FixedClock(new DateTimeOffset(date.ToDateTime(new(20, 0)), TimeSpan.Zero)),
            new AttendancePeriodLockService(fixture.Context, fixture.TenantContext));
        var approval = new LeaveRequestApprovalService(fixture.Context, identity, managerResolver, serializer,
            new FixedClock(new DateTimeOffset(date.ToDateTime(new(20, 0)), TimeSpan.Zero)), attendanceProcessor: attendanceProcessor);
        var approved = await approval.ApproveAsync(leave.RequestId);

        Assert.True(approved.Succeeded, approved.Message);
        Assert.Equal(LeaveRequestStatus.Approved, approved.Value!.Status);
        Assert.Equal(EmployeeAttendanceDayStatus.OnLeave, (await fixture.Context.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == date)).Status);
        var after = await new AttendanceOperationsService(fixture.Context, fixture.TenantContext)
            .GetOperationalExceptionsAsync(new() { FromDate = date, ToDate = date, ExceptionType = AttendanceExceptionType.Absent, Page = 1, PageSize = 10 });
        Assert.Empty(after.Value!.Items);
        var dayId = await fixture.Context.EmployeeAttendanceDays.Where(d => d.EmployeeId == fixture.EmployeeId && d.BusinessDate == date).Select(d => d.Id).SingleAsync();
        Assert.Equal(0, await fixture.Context.AttendanceExceptionResolutions.CountAsync(x => x.AttendanceDayId == dayId));
    }

    [Fact]
    public async Task Approved_leave_with_valid_punches_is_present_with_conflict_without_mutating_leave()
    {
        using var fixture = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        var leave = await AddLeaveAsync(fixture, date, LeaveRequestStatus.Approved);
        await AddPunchAsync(fixture, date, 9, PunchDirection.In);
        await AddPunchAsync(fixture, date, 18, PunchDirection.Out);

        var result = await ProcessAsync(fixture, date);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, result.Value!.Status);
        Assert.True(result.Value.LeaveConflict);
        Assert.Equal(2, result.Value.PunchCount);
        Assert.Equal(540, result.Value.WorkedMinutes);

        await AssertLeaveUnchangedAsync(fixture, leave, LeaveRequestStatus.Approved, date);
    }

    [Fact]
    public async Task Approved_leave_with_incomplete_punch_remains_incomplete_and_sets_conflict()
    {
        using var fixture = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        var leave = await AddLeaveAsync(fixture, date, LeaveRequestStatus.Approved);
        await AddPunchAsync(fixture, date, 9, PunchDirection.In);

        var result = await ProcessAsync(fixture, date);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(EmployeeAttendanceDayStatus.Incomplete, result.Value!.Status);
        Assert.True(result.Value.LeaveConflict);
        Assert.True(result.Value.HasMissingOutPunch);
        await AssertLeaveUnchangedAsync(fixture, leave, LeaveRequestStatus.Approved, date);
    }

    [Fact]
    public async Task Non_approved_leave_does_not_produce_on_leave_after_finalization()
    {
        using var fixture = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        var leave = await AddLeaveAsync(fixture, date, LeaveRequestStatus.PendingApproval);

        var result = await ProcessAsync(fixture, date);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotEqual(EmployeeAttendanceDayStatus.OnLeave, result.Value!.Status);
        Assert.Equal(EmployeeAttendanceDayStatus.Absent, result.Value.Status);
        Assert.False(result.Value.LeaveConflict);
        await AssertLeaveUnchangedAsync(fixture, leave, LeaveRequestStatus.PendingApproval, date);
    }

    [Fact]
    public async Task Approved_leave_on_adjacent_date_does_not_apply_to_processing_date()
    {
        using var fixture = await CreateFixtureAsync();
        var leaveDate = new DateOnly(2026, 10, 9);
        var processingDate = new DateOnly(2026, 10, 10);
        var leave = await AddLeaveAsync(fixture, leaveDate, LeaveRequestStatus.Approved);

        var result = await ProcessAsync(fixture, processingDate);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotEqual(EmployeeAttendanceDayStatus.OnLeave, result.Value!.Status);
        Assert.Equal(EmployeeAttendanceDayStatus.Absent, result.Value.Status);
        await AssertLeaveUnchangedAsync(fixture, leave, LeaveRequestStatus.Approved, leaveDate);
    }

    [Fact]
    public async Task Leave_lookup_is_tenant_scoped()
    {
        using var fixture = await CreateFixtureAsync();
        var date = new DateOnly(2026, 10, 10);
        var otherTenantId = Guid.NewGuid();
        var otherEmployeeId = Guid.NewGuid();

        await using (var other = fixture.CreateContext(otherTenantId, out _))
        {
            other.Tenants.Add(new Tenant { Id = otherTenantId, TenantCode = $"T-{otherTenantId:N}"[..20], Host = $"{otherTenantId:N}.test", ShardKey = otherTenantId.ToString("N"), TenantName = "Other" });
            other.Employees.Add(new Employee { Id = otherEmployeeId, TenantId = otherTenantId, EmployeeCode = "OTHER", FirstName = "Other", LastName = "Employee", Email = $"{otherEmployeeId:N}@example.test", DateOfJoining = new(2026, 1, 1) });
            other.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = Guid.NewGuid(), TenantId = otherTenantId, EmployeeId = otherEmployeeId, EffectiveFrom = new(2026, 1, 1), EmploymentStatus = EmployeeStatus.Active });
            await other.SaveChangesAsync();
            await AddLeaveAsync(other, otherTenantId, otherEmployeeId, date, LeaveRequestStatus.Approved);
        }

        var result = await ProcessAsync(fixture, date);

        Assert.True(result.Succeeded, result.Message);
        Assert.NotEqual(EmployeeAttendanceDayStatus.OnLeave, result.Value!.Status);
        Assert.False(result.Value.LeaveConflict);
    }

    private static async Task<AttendanceTestFixture> CreateFixtureAsync()
    {
        var fixture = await AttendanceTestFixture.CreateAsync();
        fixture.Shift = new Shift
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, ShiftCode = "LEAVE-TEST", ShiftName = "Leave Test",
            EffectiveFrom = new(2026, 1, 1), IsDefault = true, IsActive = true,
            StartTime = new(9, 0), EndTime = new(18, 0), PlannedDurationMinutes = 540,
            FullDayWorkMinutes = 480, MinimumWorkMinutes = 1, CaptureMode = AttendanceCaptureMode.Mixed
        };
        fixture.Context.Shifts.Add(fixture.Shift);
        await fixture.Context.SaveChangesAsync();
        return fixture;
    }

    private static Task<HRMS.Application.Common.Result<HRMS.Application.Abstractions.EmployeeAttendanceDayDto>> ProcessAsync(AttendanceTestFixture fixture, DateOnly date) =>
        new AttendanceDayProcessor(fixture.Context, fixture.TenantContext, fixture.CalendarService,
            new FixedClock(new DateTimeOffset(date.ToDateTime(new(20, 0)), TimeSpan.Zero))).ProcessAsync(fixture.EmployeeId, date);

    private static async Task AddPunchAsync(AttendanceTestFixture fixture, DateOnly date, int hour, PunchDirection direction)
    {
        fixture.Context.AttendancePunches.Add(new AttendancePunch
        {
            Id = Guid.NewGuid(), TenantId = fixture.TenantId, EmployeeId = fixture.EmployeeId,
            PunchAtUtc = new DateTime(date.Year, date.Month, date.Day, hour, 0, 0, DateTimeKind.Utc),
            BusinessDate = date, Direction = direction, Source = PunchSource.Biometric,
            ExternalPunchId = Guid.NewGuid().ToString("N"), CapturedAtUtc = DateTime.UtcNow
        });
        await fixture.Context.SaveChangesAsync();
    }

    private static Task<LeaveSeed> AddLeaveAsync(AttendanceTestFixture fixture, DateOnly date, LeaveRequestStatus status) =>
        AddLeaveAsync(fixture.Context, fixture.TenantId, fixture.EmployeeId, date, status);

    internal static Task<LeaveSeed> AddPendingLeaveAsync(HRMS.Infrastructure.Persistence.HrmsDbContext context, Guid tenantId, Guid employeeId, DateOnly date) => AddLeaveAsync(context, tenantId, employeeId, date, LeaveRequestStatus.PendingApproval);

    private static async Task<LeaveSeed> AddLeaveAsync(HRMS.Infrastructure.Persistence.HrmsDbContext context, Guid tenantId, Guid employeeId, DateOnly date, LeaveRequestStatus status)
    {
        var typeId = Guid.NewGuid(); var periodId = Guid.NewGuid(); var policyId = Guid.NewGuid(); var versionId = Guid.NewGuid(); var ruleId = Guid.NewGuid(); var requestId = Guid.NewGuid();
        var historyId = await context.EmployeeEmploymentHistory.Where(x => x.TenantId == tenantId && x.EmployeeId == employeeId).Select(x => x.Id).SingleAsync();
        context.AddRange(
            new LeaveType { Id = typeId, TenantId = tenantId, Code = $"CL-{requestId:N}"[..10], Name = "Casual Leave" },
            new LeavePeriod { Id = periodId, TenantId = tenantId, Code = $"P-{requestId:N}"[..10], Name = "Test Period", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31) },
            new LeavePolicy { Id = policyId, TenantId = tenantId, Code = $"POL-{requestId:N}"[..12], Name = "Test Policy" },
            new LeavePolicyVersion { Id = versionId, TenantId = tenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published },
            new LeavePolicyRule { Id = ruleId, TenantId = tenantId, LeavePolicyVersionId = versionId, LeaveTypeId = typeId },
            new LeaveRequest { Id = requestId, TenantId = tenantId, EmployeeId = employeeId, LeaveTypeId = typeId, LeavePeriodId = periodId, LeavePolicyVersionId = versionId, LeavePolicyRuleId = ruleId, EmployeeEmploymentHistoryId = historyId, PolicyGenderSnapshot = Gender.Unspecified, StartDate = date, EndDate = date, RequestedQuantity = 1, ChargeableQuantity = 1, Status = status, SubmittedAtUtc = DateTime.UtcNow, IdempotencyKey = $"leave-{requestId:N}", PayloadFingerprint = requestId.ToString("N"), Days = [new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = requestId, Date = date, RequestedQuantity = 1, ChargeableQuantity = 1 }] });
        await context.SaveChangesAsync();
        return new LeaveSeed(requestId, status, date);
    }

    private static async Task AssertLeaveUnchangedAsync(AttendanceTestFixture fixture, LeaveSeed seed, LeaveRequestStatus status, DateOnly date)
    {
        var request = await fixture.Context.LeaveRequests.AsNoTracking().SingleAsync(x => x.Id == seed.RequestId);
        Assert.Equal(status, request.Status);
        Assert.Equal(date, request.StartDate);
        Assert.Equal(date, request.EndDate);
        Assert.Equal(seed.RequestId, request.Id);
        Assert.Single(await fixture.Context.LeaveRequestDays.AsNoTracking().Where(x => x.LeaveRequestId == seed.RequestId).ToListAsync());
    }

    internal sealed record LeaveSeed(Guid RequestId, LeaveRequestStatus Status, DateOnly Date);

    private sealed class FixedLeaveIdentity(RuntimeEmployeeIdentity value) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(value));
    }

    private sealed class TestEmployeeSerializationLock : IEmployeeSerializationLock
    {
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
