using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRMS.Tests;

[Collection("SQL Server Leave Request Concurrency")]
public sealed class SqlServerLeaveRequestCancellationConcurrencyTests
{
    private readonly SqlServerLeaveRequestConcurrencyFixture _fixture;

    public SqlServerLeaveRequestCancellationConcurrencyTests(SqlServerLeaveRequestConcurrencyFixture fixture) => _fixture = fixture;

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Cancel_vs_cancel_has_one_winner_and_one_cancelled_event()
    {
        var requestId = await SeedApprovedAsync(new(2026, 10, 1), "cancel-cancel");
        var first = CancelAsync(requestId, _fixture.UserA);
        var second = CancelAsync(requestId, _fixture.UserA);
        await Task.WhenAll(first, second);

        var firstResult = await first;
        var secondResult = await second;
        Assert.Single(new[] { firstResult, secondResult }, result => result.Succeeded);
        Assert.Single(new[] { firstResult, secondResult }, result => !result.Succeeded && result.Status == ResultStatus.Conflict && result.Message.Contains("InvalidStatusTransition", StringComparison.Ordinal));

        var state = await ReadAsync(requestId, _fixture.TenantA);
        Assert.Equal(LeaveRequestStatus.Cancelled, state.Status);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Approved);
        var cancelled = Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Cancelled);
        Assert.Equal(_fixture.TenantA, cancelled.TenantId);
        Assert.Equal(requestId, cancelled.LeaveRequestId);
        Assert.Equal(_fixture.UserA, cancelled.ActorUserId);
        Assert.Equal(_fixture.EmployeeA, cancelled.ActorEmployeeId);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Cancel_vs_overlapping_submission_is_serialized_by_the_same_employee_lock()
    {
        var date = new DateOnly(2026, 10, 2);
        var requestId = await SeedApprovedAsync(date, "cancel-overlap");
        var cancel = CancelAsync(requestId, _fixture.UserA);
        var submit = SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, Input("cancel-overlap-new", date));
        await Task.WhenAll(cancel, submit);

        var cancelResult = await cancel;
        var submitResult = await submit;
        Assert.True(cancelResult.Succeeded, cancelResult.Message);
        var state = await ReadAsync(requestId, _fixture.TenantA);
        Assert.Equal(LeaveRequestStatus.Cancelled, state.Status);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        var newRequest = await db.LeaveRequests.SingleOrDefaultAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == "cancel-overlap-new");
        if (newRequest is null)
            Assert.Contains(LeaveRequestSubmissionErrorCodes.Overlap, submitResult.Message);
        else
            Assert.True(submitResult.Succeeded, submitResult.Message);
        Assert.Equal(1, await db.LeaveRequestEvents.CountAsync(x => x.LeaveRequestId == requestId && x.EventType == LeaveRequestEventType.Cancelled));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Cancel_vs_non_overlapping_submission_preserves_both_valid_results()
    {
        var requestId = await SeedApprovedAsync(new(2026, 10, 3), "cancel-non-overlap");
        var cancel = CancelAsync(requestId, _fixture.UserA);
        var submit = SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, Input("cancel-non-overlap-new", new(2026, 10, 4)));
        await Task.WhenAll(cancel, submit);

        Assert.True((await cancel).Succeeded);
        Assert.True((await submit).Succeeded, (await submit).Message);
        Assert.Equal(LeaveRequestStatus.Cancelled, (await ReadAsync(requestId, _fixture.TenantA)).Status);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == "cancel-non-overlap-new"));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Different_employee_cancellation_and_submission_are_isolated()
    {
        var requestId = await SeedApprovedAsync(new(2026, 10, 5), "cancel-different-employee");
        var cancel = CancelAsync(requestId, _fixture.UserA);
        var submit = SubmitAsync(_fixture.EmployeeB, _fixture.UserB, _fixture.TenantA, _fixture.EmploymentB, Input("cancel-different-employee-new", new(2026, 10, 5)));
        await Task.WhenAll(cancel, submit);

        Assert.True((await cancel).Succeeded);
        Assert.True((await submit).Succeeded, (await submit).Message);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeB && x.IdempotencyKey == "cancel-different-employee-new"));
        Assert.Equal(LeaveRequestStatus.Cancelled, (await ReadAsync(requestId, _fixture.TenantA)).Status);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Independent_tenant_cancellation_operations_preserve_tenant_and_actor_isolation()
    {
        var tenantARequest = await SeedApprovedAsync(new(2026, 10, 6), "cancel-tenant-a");
        var tenantBRequest = await _fixture.SeedRequestAsync(_fixture.TenantB, _fixture.EmployeeC, _fixture.UserC, _fixture.EmploymentC, _fixture.LeaveTypeB, _fixture.LeavePeriodB, _fixture.PolicyVersionB, _fixture.PolicyRuleB, new(2026, 10, 6), LeaveRequestStatus.Approved, "cancel-tenant-b");
        await AddApprovedEventAsync(tenantBRequest, _fixture.TenantB, _fixture.UserC, _fixture.EmployeeC);

        var results = await Task.WhenAll(
            CancelAsync(tenantARequest, _fixture.TenantA, _fixture.UserA, _fixture.EmployeeA),
            CancelAsync(tenantBRequest, _fixture.TenantB, _fixture.UserC, _fixture.EmployeeC));
        Assert.All(results, result => Assert.True(result.Succeeded, result.Message));
        var stateA = await ReadAsync(tenantARequest, _fixture.TenantA);
        var stateB = await ReadAsync(tenantBRequest, _fixture.TenantB);
        Assert.Equal(LeaveRequestStatus.Cancelled, stateA.Status);
        Assert.Equal(LeaveRequestStatus.Cancelled, stateB.Status);
        Assert.Equal(_fixture.TenantA, Assert.Single(stateA.Events, x => x.EventType == LeaveRequestEventType.Cancelled).TenantId);
        Assert.Equal(_fixture.UserA, Assert.Single(stateA.Events, x => x.EventType == LeaveRequestEventType.Cancelled).ActorUserId);
        Assert.Equal(_fixture.TenantB, Assert.Single(stateB.Events, x => x.EventType == LeaveRequestEventType.Cancelled).TenantId);
        Assert.Equal(_fixture.UserC, Assert.Single(stateB.Events, x => x.EventType == LeaveRequestEventType.Cancelled).ActorUserId);
    }

    private async Task<Guid> SeedApprovedAsync(DateOnly date, string key)
    {
        var requestId = await _fixture.SeedRequestAsync(_fixture.TenantA, _fixture.EmployeeA, _fixture.UserA, _fixture.EmploymentA, _fixture.LeaveTypeId, _fixture.LeavePeriodId, _fixture.PolicyVersionId, _fixture.PolicyRuleId, date, LeaveRequestStatus.Approved, key);
        await AddApprovedEventAsync(requestId, _fixture.TenantA, _fixture.UserA, _fixture.EmployeeA);
        return requestId;
    }

    private async Task AddApprovedEventAsync(Guid requestId, Guid tenantId, Guid userId, Guid employeeId)
    {
        await using var db = _fixture.CreateContext(tenantId, userId);
        db.LeaveRequestEvents.Add(new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = requestId, EventType = LeaveRequestEventType.Approved, OccurredAtUtc = DateTime.UtcNow, ActorType = LeaveBalanceActorType.User, ActorUserId = userId, ActorEmployeeId = employeeId });
        await db.SaveChangesAsync();
    }

    private async Task<Result<LeaveRequestCancellationResult>> CancelAsync(Guid requestId, Guid userId) =>
        await CancelAsync(requestId, _fixture.TenantA, userId, _fixture.EmployeeA);

    private async Task<Result<LeaveRequestCancellationResult>> CancelAsync(Guid requestId, Guid tenantId, Guid userId, Guid employeeId)
    {
        await using var db = _fixture.CreateContext(tenantId, userId);
        var service = new LeaveRequestCancellationService(db, new FixedIdentity(tenantId, userId, employeeId), new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System, RetryPolicy());
        return await service.CancelAsync(requestId);
    }

    private async Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(Guid employeeId, Guid userId, Guid tenantId, Guid employmentId, LeaveRequestSubmissionInput input)
    {
        await using var db = _fixture.CreateContext(tenantId, userId);
        var validation = new FixedValidation(_fixture, tenantId, employeeId, employmentId, input);
        var service = new LeaveRequestSubmissionService(db, new FixedIdentity(tenantId, userId, employeeId), validation, new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System, retryPolicy: RetryPolicy());
        return await service.SubmitAsync(input);
    }

    private LeaveRequestSubmissionRetryPolicy RetryPolicy() => new(new SqlServerLeaveRequestSubmissionDeadlockClassifier(), NullLogger<LeaveRequestSubmissionRetryPolicy>.Instance);

    private async Task<LeaveRequest> ReadAsync(Guid requestId, Guid tenantId)
    {
        await using var db = _fixture.CreateContext(tenantId);
        return await db.LeaveRequests.Include(x => x.Events).SingleAsync(x => x.TenantId == tenantId && x.Id == requestId);
    }

    private static LeaveRequestSubmissionInput Input(string key, DateOnly date) => new(Guid.Parse("10000000-0000-0000-0000-000000000001"), date, date, key);

    private sealed class FixedIdentity(Guid tenant, Guid user, Guid employee) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenant, user, employee)));
    }

    private sealed class FixedValidation(SqlServerLeaveRequestConcurrencyFixture fixture, Guid tenant, Guid employee, Guid employment, LeaveRequestSubmissionInput input) : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LeaveRequestValidationResult>.Success(new(employee, tenant == fixture.TenantA ? fixture.LeaveTypeId : fixture.LeaveTypeB, employment, tenant == fixture.TenantA ? fixture.LeavePeriodId : fixture.LeavePeriodB, tenant == fixture.TenantA ? fixture.PolicyVersionId : fixture.PolicyVersionB, tenant == fixture.TenantA ? fixture.PolicyRuleId : fixture.PolicyRuleB, Gender.Unspecified, input.StartDate, input.EndDate, 1, 1, [new LeaveRequestDayValidationResult(input.StartDate, 1, 1, null, null, true)], EntitlementMode.Unlimited, false, false, input.IdempotencyKey, Fingerprint(input), 1, 1)));

        private static string Fingerprint(LeaveRequestSubmissionInput value) => $"{value.LeaveTypeId:N}:{value.StartDate:yyyyMMdd}:{value.EndDate:yyyyMMdd}".PadRight(64, '0');
    }
}
