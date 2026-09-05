using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Application.Services;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

[CollectionDefinition("SQL Server Leave Request Concurrency", DisableParallelization = true)]
public sealed class SqlServerLeaveRequestConcurrencyCollection : ICollectionFixture<SqlServerLeaveRequestConcurrencyFixture> { }

[Collection("SQL Server Leave Request Concurrency")]
public sealed class SqlServerLeaveRequestSubmissionConcurrencyTests
{
    private readonly SqlServerLeaveRequestConcurrencyFixture _fixture;

    public SqlServerLeaveRequestSubmissionConcurrencyTests(SqlServerLeaveRequestConcurrencyFixture fixture) => _fixture = fixture;

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Same_employee_lock_blocks_until_owner_commits_but_different_employee_proceeds()
    {
        await using var first = _fixture.CreateContext(_fixture.TenantA);
        await using var second = _fixture.CreateContext(_fixture.TenantA);
        await using var third = _fixture.CreateContext(_fixture.TenantA);
        await using var tx = await first.BeginTransactionAsync();
        await new SqlServerLeaveRequestSubmissionLock(first).AcquireAsync(_fixture.TenantA, _fixture.EmployeeA);
        var sameEmployee = new SqlServerLeaveRequestSubmissionLock(second).AcquireAsync(_fixture.TenantA, _fixture.EmployeeA);
        var differentEmployee = new SqlServerLeaveRequestSubmissionLock(third).AcquireAsync(_fixture.TenantA, _fixture.EmployeeB);
        await Task.WhenAny(differentEmployee, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(differentEmployee.IsCompletedSuccessfully);
        Assert.False(sameEmployee.IsCompleted);
        await tx.CommitAsync();
        await sameEmployee.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Single_unlimited_submission_persists_request_days_and_event()
    {
        Exception? observed = null;
        var input = Input("single-sanity", _fixture.LeaveTypeId, new(2026, 9, 9), new(2026, 9, 9));
        await using (var before = _fixture.CreateContext(_fixture.TenantA))
        {
            var history = await before.EmployeeEmploymentHistory
                .SingleAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.Id == _fixture.EmploymentA);
            Assert.Equal(_fixture.TenantA, history.TenantId);
            Assert.Equal(_fixture.EmployeeA, history.EmployeeId);
            Assert.Equal(new DateOnly(2020, 1, 1), history.EffectiveFrom);
            Assert.Null(history.EffectiveTo);
            Assert.True(history.EffectiveFrom <= input.StartDate && (history.EffectiveTo is null || input.EndDate <= history.EffectiveTo));
        }
        var result = await SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, input, exception => observed = exception);
        Assert.True(result.Succeeded, DiagnosticMessage(result, observed));
        Assert.NotNull(result.Value);
        Assert.False(result.Value!.IdempotentReplay);
        Assert.Equal(LeaveRequestStatus.PendingApproval, result.Value.Status);
        Assert.Single(result.Value.RequestDays);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.Id == result.Value.RequestId));
        var persisted = await db.LeaveRequests.SingleAsync(x => x.Id == result.Value.RequestId);
        Assert.Equal(_fixture.EmploymentA, persisted.EmployeeEmploymentHistoryId);
        Assert.Equal(_fixture.EmployeeA, persisted.EmployeeId);
        Assert.Single(await db.LeaveRequestDays.Where(x => x.LeaveRequestId == result.Value.RequestId).ToListAsync());
        Assert.Single(await db.LeaveRequestEvents.Where(x => x.LeaveRequestId == result.Value.RequestId && x.EventType == LeaveRequestEventType.Submitted).ToListAsync());
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Same_key_same_payload_concurrency_persists_one_request_days_and_event()
    {
        var input = Input("same-key", _fixture.LeaveTypeId, new(2026, 9, 10), new(2026, 9, 10));
        var results = await Task.WhenAll(
            SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, input),
            SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, input));
        Assert.All(results, x => Assert.True(x.Succeeded, x.Message));
        Assert.Equal(results[0].Value!.RequestId, results[1].Value!.RequestId);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x =>
            x.TenantId == _fixture.TenantA &&
            x.EmployeeId == _fixture.EmployeeA &&
            x.IdempotencyKey == input.IdempotencyKey));
        var id = results[0].Value!.RequestId;
        Assert.Single(await db.LeaveRequestDays.Where(x => x.LeaveRequestId == id).ToListAsync());
        Assert.Single(await db.LeaveRequestEvents.Where(x => x.LeaveRequestId == id && x.EventType == LeaveRequestEventType.Submitted).ToListAsync());
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Same_key_different_payload_concurrency_returns_one_idempotency_conflict()
    {
        var first = Input("different-payload", _fixture.LeaveTypeId, new(2026, 9, 12), new(2026, 9, 12));
        var second = Input("different-payload", _fixture.LeaveTypeId, new(2026, 9, 13), new(2026, 9, 13));
        var results = await Task.WhenAll(
            SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, first),
            SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, second));
        Assert.Single(results, x => x.Succeeded);
        Assert.Single(results, x => !x.Succeeded && x.Message.Contains(LeaveRequestSubmissionErrorCodes.IdempotencyConflict, StringComparison.Ordinal));
        var winningId = results.Single(x => x.Succeeded).Value!.RequestId;
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == first.IdempotencyKey));
        Assert.Single(await db.LeaveRequestDays.Where(x => x.LeaveRequestId == winningId).ToListAsync());
        Assert.Single(await db.LeaveRequestEvents.Where(x => x.LeaveRequestId == winningId && x.EventType == LeaveRequestEventType.Submitted).ToListAsync());
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Exact_replay_returns_existing_request_before_overlap_check()
    {
        var input = Input("replay-before-overlap", _fixture.LeaveTypeId, new(2026, 9, 14), new(2026, 9, 14));
        var first = await SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, input);
        var replay = await SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, input);
        Assert.True(first.Succeeded, first.Message);
        Assert.True(replay.Succeeded, replay.Message);
        Assert.Equal(first.Value!.RequestId, replay.Value!.RequestId);
        Assert.True(replay.Value.IdempotentReplay);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == input.IdempotencyKey));
        Assert.Single(await db.LeaveRequestDays.Where(x => x.LeaveRequestId == first.Value.RequestId).ToListAsync());
        Assert.Single(await db.LeaveRequestEvents.Where(x => x.LeaveRequestId == first.Value.RequestId && x.EventType == LeaveRequestEventType.Submitted).ToListAsync());
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task PendingApproval_overlap_is_rejected_without_new_rows()
    {
        var date = new DateOnly(2026, 9, 15);
        var existingId = await SeedRequestAsync(date, LeaveRequestStatus.PendingApproval, "seed-pending");
        var result = await SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, Input("pending-overlap", _fixture.LeaveTypeId, date, date));
        Assert.False(result.Succeeded);
        Assert.Contains(LeaveRequestSubmissionErrorCodes.Overlap, result.Message);
        await AssertHistoricalRequestUnchangedAsync(existingId, LeaveRequestStatus.PendingApproval, "seed-pending");
        await AssertNoLogicalRequestAsync("pending-overlap");
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Approved_overlap_is_rejected_without_new_rows()
    {
        var date = new DateOnly(2026, 9, 16);
        var existingId = await SeedRequestAsync(date, LeaveRequestStatus.Approved, "seed-approved");
        var result = await SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, Input("approved-overlap", _fixture.LeaveTypeId, date, date));
        Assert.False(result.Succeeded);
        Assert.Contains(LeaveRequestSubmissionErrorCodes.Overlap, result.Message);
        await AssertHistoricalRequestUnchangedAsync(existingId, LeaveRequestStatus.Approved, "seed-approved");
        await AssertNoLogicalRequestAsync("approved-overlap");
    }

    [SqlServerLeaveRequestConcurrencyTheory, Trait("Category", "SqlServerIntegration")]
    [InlineData(LeaveRequestStatus.Rejected, "rejected")]
    [InlineData(LeaveRequestStatus.Withdrawn, "withdrawn")]
    [InlineData(LeaveRequestStatus.Cancelled, "cancelled")]
    public async Task Historical_non_blocking_status_allows_overlapping_submission(LeaveRequestStatus status, string label)
    {
        var date = new DateOnly(2026, 9, status switch
        {
            LeaveRequestStatus.Rejected => 17,
            LeaveRequestStatus.Withdrawn => 18,
            _ => 19
        });
        var existingId = await SeedRequestAsync(date, status, $"seed-{label}");
        var result = await SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, Input($"historical-{label}", _fixture.LeaveTypeId, date, date));
        Assert.True(result.Succeeded, result.Message);
        await AssertHistoricalRequestUnchangedAsync(existingId, status, $"seed-{label}");
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == $"historical-{label}"));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Equivalent_submissions_in_different_tenants_succeed_independently()
    {
        var date = new DateOnly(2026, 9, 20);
        var results = await Task.WhenAll(
            SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, Input("tenant-scoped", _fixture.LeaveTypeId, date, date)),
            SubmitAsync(_fixture.EmployeeC, _fixture.UserC, _fixture.TenantB, _fixture.EmploymentC, Input("tenant-scoped", _fixture.LeaveTypeB, date, date)));
        Assert.All(results, x => Assert.True(x.Succeeded, x.Message));
        Assert.NotEqual(results[0].Value!.RequestId, results[1].Value!.RequestId);
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.LeaveRequests.IgnoreQueryFilters().CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == "tenant-scoped"));
        Assert.Equal(1, await db.LeaveRequests.IgnoreQueryFilters().CountAsync(x => x.TenantId == _fixture.TenantB && x.EmployeeId == _fixture.EmployeeC && x.IdempotencyKey == "tenant-scoped"));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Allocated_submission_returns_not_ready_and_persists_nothing()
    {
        var key = "allocated-not-ready";
        var result = await SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, Input(key, _fixture.LeaveTypeId, new(2026, 9, 21), new(2026, 9, 21)), EntitlementMode.Allocated);
        Assert.False(result.Succeeded);
        Assert.Contains(LeaveRequestSubmissionErrorCodes.AllocatedBalanceReservationNotReady, result.Message);
        await AssertNoLogicalRequestAsync(key);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task NoBalanceRequired_submission_persists_request_days_and_event()
    {
        var input = Input("no-balance-required", _fixture.LeaveTypeId, new(2026, 9, 22), new(2026, 9, 22));
        var result = await SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, input, EntitlementMode.NoBalanceRequired);
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(LeaveRequestStatus.PendingApproval, result.Value!.Status);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == input.IdempotencyKey));
        Assert.Single(await db.LeaveRequestDays.Where(x => x.LeaveRequestId == result.Value.RequestId).ToListAsync());
        Assert.Single(await db.LeaveRequestEvents.Where(x => x.LeaveRequestId == result.Value.RequestId && x.EventType == LeaveRequestEventType.Submitted).ToListAsync());
        Assert.Equal(0, await db.EmployeeLeaveBalances.CountAsync(x => x.EmployeeId == _fixture.EmployeeA));
        Assert.Equal(0, await db.LeaveBalanceTransactions.CountAsync(x => x.EmployeeId == _fixture.EmployeeA));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Unsupported_configuration_persists_nothing()
    {
        var key = "unsupported-configuration";
        await using var db = _fixture.CreateContext(_fixture.TenantA, _fixture.UserA);
        var service = new LeaveRequestSubmissionService(
            db,
            new FixedIdentity(_fixture.TenantA, _fixture.UserA, _fixture.EmployeeA),
            new UnsupportedValidation(),
            new SqlServerLeaveRequestSubmissionLock(db),
            TimeProvider.System);
        var result = await service.SubmitAsync(Input(key, _fixture.LeaveTypeId, new(2026, 9, 23), new(2026, 9, 23)));
        Assert.False(result.Succeeded);
        Assert.Contains(LeaveRequestValidationErrorCodes.UnsupportedConfiguration, result.Message);
        await AssertNoLogicalRequestAsync(key);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Overlapping_new_requests_for_one_employee_have_one_winner()
    {
        var results = await Task.WhenAll(
            SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, Input("overlap-a", _fixture.LeaveTypeId, new(2026, 9, 11), new(2026, 9, 11))),
            SubmitAsync(_fixture.EmployeeA, _fixture.UserA, _fixture.TenantA, _fixture.EmploymentA, Input("overlap-b", _fixture.LeaveTypeId, new(2026, 9, 11), new(2026, 9, 11))));
        Assert.Single(results, x => x.Succeeded);
        Assert.Single(results, x => !x.Succeeded && x.Message.Contains(LeaveRequestSubmissionErrorCodes.Overlap, StringComparison.Ordinal));
    }

    private async Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(Guid employee, Guid user, Guid tenant, Guid employment, LeaveRequestSubmissionInput input, Action<Exception>? diagnosticObserver = null)
    {
        await using var db = _fixture.CreateContext(tenant, user);
        var identity = new FixedIdentity(tenant, user, employee);
        var validation = new FixedValidation(_fixture, tenant, employee, employment, input, EntitlementMode.Unlimited);
        return await new LeaveRequestSubmissionService(db, identity, validation, new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System, diagnosticObserver).SubmitAsync(input);
    }

    private async Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(Guid employee, Guid user, Guid tenant, Guid employment, LeaveRequestSubmissionInput input, EntitlementMode mode)
    {
        await using var db = _fixture.CreateContext(tenant, user);
        var identity = new FixedIdentity(tenant, user, employee);
        var validation = new FixedValidation(_fixture, tenant, employee, employment, input, mode);
        return await new LeaveRequestSubmissionService(db, identity, validation, new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System).SubmitAsync(input);
    }

    private Task<Guid> SeedRequestAsync(DateOnly date, LeaveRequestStatus status, string key) =>
        _fixture.SeedRequestAsync(_fixture.TenantA, _fixture.EmployeeA, _fixture.UserA, _fixture.EmploymentA, _fixture.LeaveTypeId, _fixture.LeavePeriodId, _fixture.PolicyVersionId, _fixture.PolicyRuleId, date, status, key);

    private async Task AssertHistoricalRequestUnchangedAsync(Guid requestId, LeaveRequestStatus status, string key)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        var request = await db.LeaveRequests.SingleAsync(x => x.Id == requestId);
        Assert.Equal(status, request.Status);
        Assert.Equal(key, request.IdempotencyKey);
        Assert.Single(await db.LeaveRequestDays.Where(x => x.LeaveRequestId == requestId).ToListAsync());
        Assert.Single(await db.LeaveRequestEvents.Where(x => x.LeaveRequestId == requestId && x.EventType == LeaveRequestEventType.Submitted).ToListAsync());
    }

    private async Task AssertNoLogicalRequestAsync(string key)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(0, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == key));
    }

    private static string DiagnosticMessage(Result<LeaveRequestSubmissionResult> result, Exception? exception) =>
        $"{result.Message}\n{FormatException(exception)}";

    private static string FormatException(Exception? exception)
    {
        if (exception is null) return "Observed exception: <none>";
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            parts.Add($"{current.GetType().FullName}: {Sanitize(current.Message)}");
        return "Observed exception: " + string.Join(" --> ", parts);
    }

    private static string Sanitize(string message) =>
        System.Text.RegularExpressions.Regex.Replace(message, @"(?i)(Password|Pwd|User Id|UID)\s*=\s*[^;]+", "$1=<redacted>");

    private static LeaveRequestSubmissionInput Input(string key, Guid leaveType, DateOnly start, DateOnly end) => new(leaveType, start, end, key);

    private sealed class FixedIdentity(Guid tenant, Guid user, Guid employee) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenant, user, employee)));
    }

    private sealed class FixedValidation(SqlServerLeaveRequestConcurrencyFixture fixture, Guid tenant, Guid employee, Guid employment, LeaveRequestSubmissionInput input, EntitlementMode mode) : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput request, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LeaveRequestValidationResult>.Success(new(employee, tenant == fixture.TenantA ? fixture.LeaveTypeId : fixture.LeaveTypeB, employment, tenant == fixture.TenantA ? fixture.LeavePeriodId : fixture.LeavePeriodB, tenant == fixture.TenantA ? fixture.PolicyVersionId : fixture.PolicyVersionB, tenant == fixture.TenantA ? fixture.PolicyRuleId : fixture.PolicyRuleB, Gender.Unspecified, input.StartDate, input.EndDate, 1, input.EndDate.DayNumber - input.StartDate.DayNumber + 1, Enumerable.Range(0, input.EndDate.DayNumber - input.StartDate.DayNumber + 1).Select(i => new LeaveRequestDayValidationResult(input.StartDate.AddDays(i), 1, 1, null, null, true)).ToArray(), mode, mode == EntitlementMode.Allocated, false, input.IdempotencyKey, Fingerprint(input), 1, 1)));

        private static string Fingerprint(LeaveRequestSubmissionInput value) => $"{value.LeaveTypeId:N}:{value.StartDate:yyyyMMdd}:{value.EndDate:yyyyMMdd}".PadRight(64, '0');
    }

    private sealed class UnsupportedValidation : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput input, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LeaveRequestValidationResult>.Invalid("configuration", "UnsupportedConfiguration: SQL Server validation test uses the existing unsupported-configuration path."));
    }
}
