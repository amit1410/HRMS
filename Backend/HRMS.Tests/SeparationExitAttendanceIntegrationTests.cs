using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Attendance;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationExitAttendanceIntegrationTests
{
    [Fact]
    public async Task Exit_preserves_attendance_history_and_denies_post_exit_workflow_requests()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await SeparationExitExecutionTests.ReadyFixtureAsync(database);
        var historicalDate = fixture.FinalLwd.AddDays(-1);

        await using (var setup = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            setup.EmployeeAttendanceDays.Add(new EmployeeAttendanceDay
            {
                Id = Guid.NewGuid(),
                TenantId = fixture.TenantId,
                EmployeeId = fixture.EmployeeId,
                BusinessDate = historicalDate,
                Status = EmployeeAttendanceDayStatus.Present,
                WorkedMinutes = 480,
                FirstPunchAtUtc = historicalDate.ToDateTime(new(9, 0), DateTimeKind.Utc),
                LastPunchAtUtc = historicalDate.ToDateTime(new(17, 0), DateTimeKind.Utc)
            });
            await setup.SaveChangesAsync();
        }

        await using (var execute = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            var result = await new SeparationExitService(execute, new TestTenantContext(fixture.TenantId, fixture.HrUserId), TimeProvider.System).ExecuteAsync(fixture.SeparationId, new());
            Assert.True(result.Succeeded, result.Message);
        }

        await using var verify = database.CreateIsolatedContext(new TestTenantContext(fixture.TenantId, fixture.EmployeeUserId));
        var historical = await verify.EmployeeAttendanceDays.SingleAsync(x => x.EmployeeId == fixture.EmployeeId && x.BusinessDate == historicalDate);
        Assert.Equal(EmployeeAttendanceDayStatus.Present, historical.Status);
        Assert.Equal(480, historical.WorkedMinutes);
        Assert.Equal(historicalDate.ToDateTime(new(9, 0), DateTimeKind.Utc), historical.FirstPunchAtUtc);
        Assert.Equal(historicalDate.ToDateTime(new(17, 0), DateTimeKind.Utc), historical.LastPunchAtUtc);

        var service = new AttendanceWorkflowService(
            verify,
            new FixedIdentity(fixture.TenantId, fixture.EmployeeUserId, fixture.EmployeeId),
            new NoManager(),
            new UnusedProcessor(),
            new FixedClock(new DateTimeOffset(2026, 10, 20, 20, 0, 0, TimeSpan.Zero)));
        var postExit = fixture.FinalLwd.AddDays(1);
        var regularization = await service.SubmitRegularizationAsync(new(postExit, AttendanceRegularizationType.MissingOutPunch, null, postExit.ToDateTime(new(18, 0), DateTimeKind.Utc), "post-exit"));
        var onDuty = await service.SubmitOnDutyAsync(new(postExit, postExit, "post-exit"));
        Assert.Equal(ResultStatus.Forbidden, regularization.Status);
        Assert.Equal(ResultStatus.Forbidden, onDuty.Status);
        Assert.Equal(1, await verify.EmployeeAttendanceDays.CountAsync(x => x.EmployeeId == fixture.EmployeeId));
    }

    private sealed class FixedIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, employeeId)));
    }

    private sealed class NoManager : IEmployeeManagerResolver
    {
        public Task<Result<EmployeeManagerResolution>> ResolveAsync(Guid employeeId, DateOnly asOfDate, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<EmployeeManagerResolution>.Success(new(EmployeeManagerResolutionStatus.NoAssignedManager, employeeId, null, null, null, "none")));

        public Task<bool> WouldCreateCycleAsync(Guid employeeId, Guid proposedManagerId, DateOnly asOfDate, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class UnusedProcessor : IAttendanceDayProcessor
    {
        public Task<Result<EmployeeAttendanceDayDto>> ProcessAsync(Guid employeeId, DateOnly businessDate, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The processor must not run for an inactive employee.");
    }
}
