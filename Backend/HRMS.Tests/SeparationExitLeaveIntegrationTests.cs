using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class SeparationExitLeaveIntegrationTests
{
    [Fact]
    public async Task Exit_preserves_approved_leave_and_denies_new_post_exit_request()
    {
        using var database = new SqliteInMemoryDatabase();
        var fixture = await SeparationExitExecutionTests.ReadyFixtureAsync(database);
        var leaveTypeId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var ruleId = Guid.NewGuid();
        var historyId = Guid.NewGuid();
        var requestId = Guid.NewGuid();
        var leaveDate = fixture.FinalLwd.AddDays(-2);

        await using (var setup = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            setup.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory
            {
                Id = historyId,
                TenantId = fixture.TenantId,
                EmployeeId = fixture.EmployeeId,
                EffectiveFrom = fixture.FinalLwd.AddYears(-1),
                EffectiveTo = fixture.FinalLwd,
                EmploymentStatus = EmployeeStatus.Active
            });
            setup.LeaveTypes.Add(new LeaveType { Id = leaveTypeId, TenantId = fixture.TenantId, Code = "EXIT", Name = "Exit Test Leave" });
            setup.LeavePeriods.Add(new LeavePeriod { Id = periodId, TenantId = fixture.TenantId, Code = "EXIT-P", Name = "Exit Test Period", StartDate = fixture.FinalLwd.AddYears(-1), EndDate = fixture.FinalLwd.AddYears(1) });
            setup.LeavePolicies.Add(new LeavePolicy { Id = policyId, TenantId = fixture.TenantId, Code = "EXIT-LP", Name = "Exit Test Policy" });
            setup.LeavePolicyVersions.Add(new LeavePolicyVersion { Id = versionId, TenantId = fixture.TenantId, LeavePolicyId = policyId, VersionNumber = 1, EffectiveFrom = fixture.FinalLwd.AddYears(-1), Status = LeavePolicyVersionStatus.Published });
            setup.LeavePolicyRules.Add(new LeavePolicyRule { Id = ruleId, TenantId = fixture.TenantId, LeavePolicyVersionId = versionId, LeaveTypeId = leaveTypeId });
            setup.LeaveRequests.Add(new LeaveRequest
            {
                Id = requestId,
                TenantId = fixture.TenantId,
                EmployeeId = fixture.EmployeeId,
                LeaveTypeId = leaveTypeId,
                LeavePeriodId = periodId,
                LeavePolicyVersionId = versionId,
                LeavePolicyRuleId = ruleId,
                EmployeeEmploymentHistoryId = historyId,
                StartDate = leaveDate,
                EndDate = leaveDate,
                RequestedQuantity = 1,
                ChargeableQuantity = 1,
                Status = LeaveRequestStatus.Approved,
                SubmittedAtUtc = DateTime.UtcNow,
                IdempotencyKey = "exit-history",
                PayloadFingerprint = "exit-history"
            });
            setup.LeaveRequestDays.Add(new LeaveRequestDay { Id = Guid.NewGuid(), TenantId = fixture.TenantId, LeaveRequestId = requestId, Date = leaveDate, RequestedQuantity = 1, ChargeableQuantity = 1 });
            await setup.SaveChangesAsync();
        }

        await using (var execute = database.CreateContext(new TestTenantContext(fixture.TenantId, fixture.HrUserId)))
        {
            var result = await new SeparationExitService(execute, new TestTenantContext(fixture.TenantId, fixture.HrUserId), TimeProvider.System).ExecuteAsync(fixture.SeparationId, new());
            Assert.True(result.Succeeded, result.Message);
        }

        await using var verify = database.CreateIsolatedContext(new TestTenantContext(fixture.TenantId, fixture.EmployeeUserId));
        var preserved = await verify.LeaveRequests.SingleAsync(x => x.Id == requestId);
        Assert.Equal(LeaveRequestStatus.Approved, preserved.Status);
        Assert.Single(await verify.LeaveRequestDays.Where(x => x.LeaveRequestId == requestId).ToListAsync());

        var validation = new FixedValidationService(new LeaveRequestValidationResult(
            fixture.EmployeeId, leaveTypeId, historyId, periodId, versionId, ruleId, Gender.Unspecified,
            fixture.FinalLwd.AddDays(1), fixture.FinalLwd.AddDays(1), 1, 1,
            [new(fixture.FinalLwd.AddDays(1), 1, 1, null, null, true)], EntitlementMode.Unlimited,
            false, false, "post-exit", "post-exit", 1, 1));
        var submission = new LeaveRequestSubmissionService(
            verify,
            new FixedIdentity(fixture.TenantId, fixture.EmployeeUserId, fixture.EmployeeId),
            validation,
            new NoOpLock(),
            TimeProvider.System);

        var denied = await submission.SubmitAsync(new(leaveTypeId, fixture.FinalLwd.AddDays(1), fixture.FinalLwd.AddDays(1), "post-exit"));
        Assert.Equal(ResultStatus.Forbidden, denied.Status);
        Assert.Equal(1, await verify.LeaveRequests.CountAsync(x => x.EmployeeId == fixture.EmployeeId));
    }

    private sealed class FixedIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, employeeId)));
    }

    private sealed class FixedValidationService(LeaveRequestValidationResult value) : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LeaveRequestValidationResult>.Success(value));
    }

    private sealed class NoOpLock : IEmployeeSerializationLock
    {
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
