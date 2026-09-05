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
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

[Collection("SQL Server Leave Request Concurrency")]
public sealed class SqlServerLeaveRequestApprovalConcurrencyTests
{
    private readonly SqlServerLeaveRequestConcurrencyFixture _fixture;

    public SqlServerLeaveRequestApprovalConcurrencyTests(SqlServerLeaveRequestConcurrencyFixture fixture) => _fixture = fixture;

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Approve_vs_approve_has_one_winner_and_one_approved_event()
    {
        await PrepareApproverAsync();
        var requestId = await SeedRequestAsync(new(2026, 11, 1), "approval-approve-approve");

        var results = await Task.WhenAll(
            ApproveAsync(requestId),
            ApproveAsync(requestId));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.Status == ResultStatus.Conflict);
        var state = await ReadAsync(requestId);
        Assert.Equal(LeaveRequestStatus.Approved, state.Status);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        var approved = Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Approved);
        Assert.Equal(_fixture.UserB, approved.ActorUserId);
        Assert.Equal(_fixture.EmployeeB, approved.ActorEmployeeId);
        Assert.Empty(state.Events.Where(x => x.EventType == LeaveRequestEventType.Rejected));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Approve_vs_reject_has_one_terminal_winner_and_one_terminal_event()
    {
        await PrepareApproverAsync();
        var requestId = await SeedRequestAsync(new(2026, 11, 2), "approval-approve-reject");

        var results = await Task.WhenAll(
            ApproveAsync(requestId),
            RejectAsync(requestId));

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.Status == ResultStatus.Conflict);
        var state = await ReadAsync(requestId);
        Assert.True(state.Status is LeaveRequestStatus.Approved or LeaveRequestStatus.Rejected);
        Assert.Single(state.Events, x => x.EventType is LeaveRequestEventType.Approved or LeaveRequestEventType.Rejected);
        Assert.Single(state.Events.Where(x => x.EventType == LeaveRequestEventType.Approved || x.EventType == LeaveRequestEventType.Rejected));
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Reject_vs_overlapping_submission_is_serialized_by_the_same_employee_lock()
    {
        await PrepareApproverAsync();
        var date = new DateOnly(2026, 11, 3);
        var requestId = await SeedRequestAsync(date, "approval-reject-overlap");
        var input = Input("approval-reject-overlap-new", date);

        var rejectTask = RejectAsync(requestId);
        var submitTask = SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.EmploymentA, input);
        await Task.WhenAll(rejectTask, submitTask);
        var rejectResult = await rejectTask;
        var submitResult = await submitTask;

        Assert.True(rejectResult.Succeeded, rejectResult.Message);
        var state = await ReadAsync(requestId);
        Assert.Equal(LeaveRequestStatus.Rejected, state.Status);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        var newRequest = await db.LeaveRequests.SingleOrDefaultAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == input.IdempotencyKey);
        if (newRequest is null)
            Assert.Contains(LeaveRequestSubmissionErrorCodes.Overlap, submitResult.Message);
        else
            Assert.True(submitResult.Succeeded, submitResult.Message);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Approve_vs_overlapping_submission_keeps_the_new_request_blocked()
    {
        await PrepareApproverAsync();
        var date = new DateOnly(2026, 11, 4);
        var requestId = await SeedRequestAsync(date, "approval-approve-overlap");
        var input = Input("approval-approve-overlap-new", date);

        var approveTask = ApproveAsync(requestId);
        var submitTask = SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.EmploymentA, input);
        await Task.WhenAll(approveTask, submitTask);
        var approveResult = await approveTask;
        var submitResult = await submitTask;

        Assert.True(approveResult.Succeeded, approveResult.Message);
        Assert.False(submitResult.Succeeded);
        Assert.Contains(LeaveRequestSubmissionErrorCodes.Overlap, submitResult.Message);
        var state = await ReadAsync(requestId);
        Assert.Equal(LeaveRequestStatus.Approved, state.Status);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Null(await db.LeaveRequests.SingleOrDefaultAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == input.IdempotencyKey));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Different_employee_submission_proceeds_while_approval_uses_employee_a_lock()
    {
        await PrepareApproverAsync();
        var requestId = await SeedRequestAsync(new(2026, 11, 5), "approval-different-employee");
        var input = Input("approval-different-employee-new", new(2026, 11, 6));

        var approveTask = ApproveAsync(requestId);
        var submitTask = SubmitAsync(_fixture.EmployeeB, _fixture.UserB, _fixture.EmploymentB, input);
        await Task.WhenAll(approveTask, submitTask);
        var approveResult = await approveTask;
        var submitResult = await submitTask;

        Assert.True(approveResult.Succeeded, approveResult.Message);
        Assert.True(submitResult.Succeeded, submitResult.Message);
        var state = await ReadAsync(requestId);
        Assert.Equal(LeaveRequestStatus.Approved, state.Status);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeB && x.IdempotencyKey == input.IdempotencyKey));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Cross_tenant_request_is_not_visible_to_approval_service()
    {
        await PrepareApproverAsync();
        var requestId = await _fixture.SeedRequestAsync(_fixture.TenantB, _fixture.EmployeeC, _fixture.UserC, _fixture.EmploymentC, _fixture.LeaveTypeB, _fixture.LeavePeriodB, _fixture.PolicyVersionB, _fixture.PolicyRuleB, new(2026, 11, 7), LeaveRequestStatus.PendingApproval, "approval-cross-tenant");

        var result = await ApproveAsync(requestId);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        await using var db = _fixture.CreateContext(_fixture.TenantB);
        var request = await db.LeaveRequests.SingleAsync(x => x.TenantId == _fixture.TenantB && x.Id == requestId);
        Assert.Equal(LeaveRequestStatus.PendingApproval, request.Status);
        Assert.Empty(await db.LeaveRequestEvents.Where(x => x.LeaveRequestId == requestId && x.EventType == LeaveRequestEventType.Approved).ToListAsync());
    }

    private async Task PrepareApproverAsync()
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        var employee = await db.Employees.SingleAsync(x => x.Id == _fixture.EmployeeA);
        employee.ReportingManagerId = _fixture.EmployeeB;
        var history = await db.EmployeeEmploymentHistory.SingleAsync(x => x.Id == _fixture.EmploymentA);
        history.ManagerId = _fixture.EmployeeB;

        if (!await db.Roles.AnyAsync(x => x.Id == 900))
            db.Roles.Add(new Role { Id = 900, Name = "SQL Approval Test Role" });
        if (!await db.Permissions.AnyAsync(x => x.Id == 35))
            db.Permissions.Add(new Permission { Id = 35, Name = Permissions.Leave.Approve });
        if (!await db.RolePermissions.AnyAsync(x => x.RoleId == 900 && x.PermissionId == 35))
            db.RolePermissions.Add(new RolePermission { RoleId = 900, PermissionId = 35 });
        if (!await db.UserRoles.AnyAsync(x => x.TenantId == _fixture.TenantA && x.UserId == _fixture.UserB && x.RoleId == 900))
            db.UserRoles.Add(new UserRole { TenantId = _fixture.TenantA, UserId = _fixture.UserB, RoleId = 900 });

        if (!await db.AccountEmployeeCurrentLinks.AnyAsync(x => x.TenantId == _fixture.TenantA && x.UserId == _fixture.UserB))
        {
            var linkId = Guid.NewGuid();
            db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
            {
                Id = linkId,
                TenantId = _fixture.TenantA,
                SubjectUserId = _fixture.UserB,
                ActorUserId = _fixture.UserB,
                Sequence = 1,
                Operation = "Link",
                NewLinkId = linkId,
                AfterEmployeeId = _fixture.EmployeeB,
                OccurredAtUtc = DateTime.UtcNow,
                Reason = "SQL approval concurrency test setup",
                CorrelationId = Guid.NewGuid().ToString("N")
            });
            db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink
            {
                LinkId = linkId,
                TenantId = _fixture.TenantA,
                UserId = _fixture.UserB,
                EmployeeId = _fixture.EmployeeB
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedRequestAsync(DateOnly date, string key) =>
        await _fixture.SeedRequestAsync(_fixture.TenantA, _fixture.EmployeeA, _fixture.UserA, _fixture.EmploymentA, _fixture.LeaveTypeId, _fixture.LeavePeriodId, _fixture.PolicyVersionId, _fixture.PolicyRuleId, date, LeaveRequestStatus.PendingApproval, key);

    private async Task<Result<LeaveRequestApprovalResult>> ApproveAsync(Guid requestId)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, _fixture.UserB);
        return await CreateApprovalService(db).ApproveAsync(requestId);
    }

    private async Task<Result<LeaveRequestApprovalResult>> RejectAsync(Guid requestId)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, _fixture.UserB);
        return await CreateApprovalService(db).RejectAsync(requestId);
    }

    private LeaveRequestApprovalService CreateApprovalService(HrmsDbContext db)
    {
        var tenantContext = new TestTenantContext(_fixture.TenantA, _fixture.UserB);
        var classifier = new SqlServerLeaveRequestSubmissionDeadlockClassifier();
        return new LeaveRequestApprovalService(
            db,
            new EmployeeIdentityResolver(db, tenantContext),
            new EmployeeManagerResolver(db, tenantContext),
            new SqlServerLeaveRequestSubmissionLock(db),
            TimeProvider.System,
            new LeaveRequestSubmissionRetryPolicy(classifier, NullLogger<LeaveRequestSubmissionRetryPolicy>.Instance),
            classifier);
    }

    private async Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(Guid employeeId, Guid userId, Guid employmentId, LeaveRequestSubmissionInput input)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, userId);
        return await new LeaveRequestSubmissionService(
            db,
            new FixedIdentity(_fixture.TenantA, userId, employeeId),
            new FixedValidation(_fixture, employeeId, employmentId, input),
            new SqlServerLeaveRequestSubmissionLock(db),
            TimeProvider.System).SubmitAsync(input);
    }

    private async Task<LeaveRequest> ReadAsync(Guid requestId)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        return await db.LeaveRequests.Include(x => x.Events).SingleAsync(x => x.TenantId == _fixture.TenantA && x.Id == requestId);
    }

    private static LeaveRequestSubmissionInput Input(string key, DateOnly date) =>
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), date, date, key);

    private sealed class FixedIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, employeeId)));
    }

    private sealed class FixedValidation(SqlServerLeaveRequestConcurrencyFixture fixture, Guid employeeId, Guid employmentId, LeaveRequestSubmissionInput input) : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LeaveRequestValidationResult>.Success(new(
                employeeId, fixture.LeaveTypeId, employmentId, fixture.LeavePeriodId, fixture.PolicyVersionId, fixture.PolicyRuleId,
                Gender.Unspecified, input.StartDate, input.EndDate, 1, 1,
                [new LeaveRequestDayValidationResult(input.StartDate, 1, 1, null, null, true)],
                EntitlementMode.Unlimited, false, false, input.IdempotencyKey,
                $"{input.LeaveTypeId:N}:{input.StartDate:yyyyMMdd}:{input.EndDate:yyyyMMdd}".PadRight(64, '0'), 1, 1)));
    }
}
