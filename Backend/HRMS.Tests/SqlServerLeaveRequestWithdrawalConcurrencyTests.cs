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
public sealed class SqlServerLeaveRequestWithdrawalConcurrencyTests
{
    private readonly SqlServerLeaveRequestConcurrencyFixture _fixture;

    public SqlServerLeaveRequestWithdrawalConcurrencyTests(SqlServerLeaveRequestConcurrencyFixture fixture) => _fixture = fixture;

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Withdraw_vs_withdraw_has_one_winner_and_one_withdrawn_event()
    {
        await PrepareActorsAsync();
        var requestId = await SeedRequestAsync(new(2026, 12, 1), "withdraw-withdraw");
        var first = WithdrawAsync(requestId, _fixture.UserA);
        var second = WithdrawAsync(requestId, _fixture.UserA);
        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result.Succeeded);
        Assert.Single(results, result => !result.Succeeded && result.Status == ResultStatus.Conflict);
        var state = await ReadAsync(requestId, _fixture.TenantA);
        Assert.Equal(LeaveRequestStatus.Withdrawn, state.Status);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        var withdrawn = Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Withdrawn);
        Assert.Equal(_fixture.UserA, withdrawn.ActorUserId);
        Assert.Equal(_fixture.EmployeeA, withdrawn.ActorEmployeeId);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Withdraw_vs_approve_has_one_terminal_winner_and_one_terminal_event()
    {
        await PrepareActorsAsync();
        var requestId = await SeedRequestAsync(new(2026, 12, 2), "withdraw-approve");
        var withdraw = WithdrawAsync(requestId, _fixture.UserA);
        var approve = ApproveAsync(requestId);
        await Task.WhenAll(withdraw, approve);
        var withdrawResult = await withdraw;
        var approveResult = await approve;

        Assert.NotEqual(withdrawResult.Succeeded, approveResult.Succeeded);
        Assert.True(
            (!withdrawResult.Succeeded && withdrawResult.Status == ResultStatus.Conflict) ||
            (!approveResult.Succeeded && approveResult.Status == ResultStatus.Conflict));
        var state = await ReadAsync(requestId, _fixture.TenantA);
        Assert.True(state.Status is LeaveRequestStatus.Withdrawn or LeaveRequestStatus.Approved);
        Assert.Single(state.Events.Where(x => x.EventType == LeaveRequestEventType.Withdrawn || x.EventType == LeaveRequestEventType.Approved));
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Withdraw_vs_reject_has_one_terminal_winner_and_one_terminal_event()
    {
        await PrepareActorsAsync();
        var requestId = await SeedRequestAsync(new(2026, 12, 3), "withdraw-reject");
        var withdraw = WithdrawAsync(requestId, _fixture.UserA);
        var reject = RejectAsync(requestId);
        await Task.WhenAll(withdraw, reject);
        var withdrawResult = await withdraw;
        var rejectResult = await reject;

        Assert.NotEqual(withdrawResult.Succeeded, rejectResult.Succeeded);
        Assert.True(
            (!withdrawResult.Succeeded && withdrawResult.Status == ResultStatus.Conflict) ||
            (!rejectResult.Succeeded && rejectResult.Status == ResultStatus.Conflict));
        var state = await ReadAsync(requestId, _fixture.TenantA);
        Assert.True(state.Status is LeaveRequestStatus.Withdrawn or LeaveRequestStatus.Rejected);
        Assert.Single(state.Events.Where(x => x.EventType == LeaveRequestEventType.Withdrawn || x.EventType == LeaveRequestEventType.Rejected));
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Withdraw_vs_overlapping_submission_is_serialized_by_the_same_employee_lock()
    {
        await PrepareActorsAsync();
        var date = new DateOnly(2026, 12, 4);
        var requestId = await SeedRequestAsync(date, "withdraw-overlap");
        var input = Input("withdraw-overlap-new", date);
        var withdraw = WithdrawAsync(requestId, _fixture.UserA);
        var submit = SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.EmploymentA, input);
        await Task.WhenAll(withdraw, submit);
        var withdrawal = await withdraw;
        var submission = await submit;

        Assert.True(withdrawal.Succeeded, withdrawal.Message);
        var state = await ReadAsync(requestId, _fixture.TenantA);
        Assert.Equal(LeaveRequestStatus.Withdrawn, state.Status);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        var newRequest = await db.LeaveRequests.SingleOrDefaultAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == input.IdempotencyKey);
        if (newRequest is null)
            Assert.Contains(LeaveRequestSubmissionErrorCodes.Overlap, submission.Message);
        else
            Assert.True(submission.Succeeded, submission.Message);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Withdraw_vs_non_overlapping_submission_preserves_both_valid_results()
    {
        await PrepareActorsAsync();
        var requestId = await SeedRequestAsync(new(2026, 12, 5), "withdraw-non-overlap");
        var input = Input("withdraw-non-overlap-new", new(2026, 12, 6));
        var withdraw = WithdrawAsync(requestId, _fixture.UserA);
        var submit = SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.EmploymentA, input);
        await Task.WhenAll(withdraw, submit);

        var withdrawal = await withdraw;
        var submission = await submit;
        Assert.True(withdrawal.Succeeded, withdrawal.Message);
        Assert.True(submission.Succeeded, submission.Message);
        var state = await ReadAsync(requestId, _fixture.TenantA);
        Assert.Equal(LeaveRequestStatus.Withdrawn, state.Status);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == input.IdempotencyKey));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Different_employee_withdrawal_and_submission_are_isolated()
    {
        await PrepareActorsAsync();
        var requestId = await SeedRequestAsync(new(2026, 12, 7), "withdraw-different-employee");
        var input = Input("withdraw-different-employee-new", new(2026, 12, 8));
        var withdraw = WithdrawAsync(requestId, _fixture.UserA);
        var submit = SubmitAsync(_fixture.EmployeeB, _fixture.UserB, _fixture.EmploymentB, input);
        await Task.WhenAll(withdraw, submit);

        var withdrawal = await withdraw;
        var submission = await submit;
        Assert.True(withdrawal.Succeeded, withdrawal.Message);
        Assert.True(submission.Succeeded, submission.Message);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeB && x.IdempotencyKey == input.IdempotencyKey));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Cross_tenant_withdrawal_is_isolated()
    {
        await PrepareActorsAsync();
        var requestId = await _fixture.SeedRequestAsync(_fixture.TenantB, _fixture.EmployeeC, _fixture.UserC, _fixture.EmploymentC, _fixture.LeaveTypeB, _fixture.LeavePeriodB, _fixture.PolicyVersionB, _fixture.PolicyRuleB, new(2026, 12, 9), LeaveRequestStatus.PendingApproval, "withdraw-cross-tenant");

        var result = await WithdrawAsync(requestId, _fixture.UserA);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        var state = await ReadAsync(requestId, _fixture.TenantB);
        Assert.Equal(LeaveRequestStatus.PendingApproval, state.Status);
        Assert.DoesNotContain(state.Events, x => x.EventType == LeaveRequestEventType.Withdrawn);
    }

    private async Task PrepareActorsAsync()
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        var employee = await db.Employees.SingleAsync(x => x.Id == _fixture.EmployeeA);
        employee.ReportingManagerId = _fixture.EmployeeB;
        var history = await db.EmployeeEmploymentHistory.SingleAsync(x => x.Id == _fixture.EmploymentA);
        history.ManagerId = _fixture.EmployeeB;
        if (!await db.Roles.AnyAsync(x => x.Id == 900)) db.Roles.Add(new Role { Id = 900, Name = "SQL Approval Test Role" });
        if (!await db.Permissions.AnyAsync(x => x.Id == 35)) db.Permissions.Add(new Permission { Id = 35, Name = Permissions.Leave.Approve });
        if (!await db.RolePermissions.AnyAsync(x => x.RoleId == 900 && x.PermissionId == 35)) db.RolePermissions.Add(new RolePermission { RoleId = 900, PermissionId = 35 });
        if (!await db.UserRoles.AnyAsync(x => x.TenantId == _fixture.TenantA && x.UserId == _fixture.UserB && x.RoleId == 900)) db.UserRoles.Add(new UserRole { TenantId = _fixture.TenantA, UserId = _fixture.UserB, RoleId = 900 });
        await EnsureLinkAsync(db, _fixture.TenantA, _fixture.UserA, _fixture.EmployeeA, "owner setup");
        await EnsureLinkAsync(db, _fixture.TenantA, _fixture.UserB, _fixture.EmployeeB, "manager setup");
        await db.SaveChangesAsync();
    }

    private static async Task EnsureLinkAsync(HrmsDbContext db, Guid tenantId, Guid userId, Guid employeeId, string reason)
    {
        if (await db.AccountEmployeeCurrentLinks.AnyAsync(x => x.TenantId == tenantId && x.UserId == userId)) return;
        var linkId = Guid.NewGuid();
        db.AccountEmployeeLinkEvents.Add(new AccountEmployeeLinkEvent
        {
            Id = linkId, TenantId = tenantId, SubjectUserId = userId, ActorUserId = userId,
            Sequence = 1, Operation = "Link", NewLinkId = linkId, AfterEmployeeId = employeeId,
            OccurredAtUtc = DateTime.UtcNow, Reason = reason, CorrelationId = Guid.NewGuid().ToString("N")
        });
        db.AccountEmployeeCurrentLinks.Add(new AccountEmployeeCurrentLink { LinkId = linkId, TenantId = tenantId, UserId = userId, EmployeeId = employeeId });
    }

    private async Task<Guid> SeedRequestAsync(DateOnly date, string key) =>
        await _fixture.SeedRequestAsync(_fixture.TenantA, _fixture.EmployeeA, _fixture.UserA, _fixture.EmploymentA, _fixture.LeaveTypeId, _fixture.LeavePeriodId, _fixture.PolicyVersionId, _fixture.PolicyRuleId, date, LeaveRequestStatus.PendingApproval, key);

    private async Task<Result<LeaveRequestWithdrawalResult>> WithdrawAsync(Guid requestId, Guid userId)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, userId);
        var tenantContext = new TestTenantContext(_fixture.TenantA, userId);
        var classifier = new SqlServerLeaveRequestSubmissionDeadlockClassifier();
        return await new LeaveRequestWithdrawalService(db, new EmployeeIdentityResolver(db, tenantContext), new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System, new LeaveRequestSubmissionRetryPolicy(classifier, NullLogger<LeaveRequestSubmissionRetryPolicy>.Instance), classifier).WithdrawAsync(requestId);
    }

    private async Task<Result<LeaveRequestApprovalResult>> ApproveAsync(Guid requestId)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, _fixture.UserB);
        var tenantContext = new TestTenantContext(_fixture.TenantA, _fixture.UserB);
        var classifier = new SqlServerLeaveRequestSubmissionDeadlockClassifier();
        return await new LeaveRequestApprovalService(db, new EmployeeIdentityResolver(db, tenantContext), new EmployeeManagerResolver(db, tenantContext), new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System, new LeaveRequestSubmissionRetryPolicy(classifier, NullLogger<LeaveRequestSubmissionRetryPolicy>.Instance), classifier).ApproveAsync(requestId);
    }

    private async Task<Result<LeaveRequestApprovalResult>> RejectAsync(Guid requestId)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, _fixture.UserB);
        var tenantContext = new TestTenantContext(_fixture.TenantA, _fixture.UserB);
        var classifier = new SqlServerLeaveRequestSubmissionDeadlockClassifier();
        return await new LeaveRequestApprovalService(db, new EmployeeIdentityResolver(db, tenantContext), new EmployeeManagerResolver(db, tenantContext), new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System, new LeaveRequestSubmissionRetryPolicy(classifier, NullLogger<LeaveRequestSubmissionRetryPolicy>.Instance), classifier).RejectAsync(requestId);
    }

    private async Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(Guid employeeId, Guid userId, Guid employmentId, LeaveRequestSubmissionInput input)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, userId);
        return await new LeaveRequestSubmissionService(db, new FixedIdentity(_fixture.TenantA, userId, employeeId), new FixedValidation(_fixture, employeeId, employmentId, input), new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System).SubmitAsync(input);
    }

    private async Task<LeaveRequest> ReadAsync(Guid requestId, Guid tenantId)
    {
        await using var db = _fixture.CreateContext(tenantId);
        return await db.LeaveRequests.Include(x => x.Events).SingleAsync(x => x.TenantId == tenantId && x.Id == requestId);
    }

    private static LeaveRequestSubmissionInput Input(string key, DateOnly date) =>
        new(Guid.Parse("10000000-0000-0000-0000-000000000001"), date, date, key);

    private sealed class FixedIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, employeeId)));
    }

    private sealed class FixedValidation(SqlServerLeaveRequestConcurrencyFixture fixture, Guid employeeId, Guid employmentId, LeaveRequestSubmissionInput input) : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LeaveRequestValidationResult>.Success(new(employeeId, fixture.LeaveTypeId, employmentId, fixture.LeavePeriodId, fixture.PolicyVersionId, fixture.PolicyRuleId, Gender.Unspecified, input.StartDate, input.EndDate, 1, 1, [new LeaveRequestDayValidationResult(input.StartDate, 1, 1, null, null, true)], EntitlementMode.Unlimited, false, false, input.IdempotencyKey, new string('0', 64), 1, 1)));
    }
}
