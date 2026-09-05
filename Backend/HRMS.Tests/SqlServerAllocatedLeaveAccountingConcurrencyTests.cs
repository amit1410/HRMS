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
public sealed class SqlServerAllocatedLeaveAccountingConcurrencyTests
{
    private readonly SqlServerLeaveRequestConcurrencyFixture _fixture;

    public SqlServerAllocatedLeaveAccountingConcurrencyTests(SqlServerLeaveRequestConcurrencyFixture fixture) => _fixture = fixture;

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task SqlServer_decimal_balance_check_accepts_valid_values_and_rejects_invalid_values()
    {
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, granted: 20.000m, reserved: 5.500m, consumed: 3.250m);
        await using (var db = _fixture.CreateContext(_fixture.TenantA))
        {
            var balance = await db.EmployeeLeaveBalances.SingleAsync(x => x.Id == scenario.BalanceId);
            Assert.Equal(20.000m, balance.GrantedQuantity);
            Assert.Equal(5.500m, balance.ReservedQuantity);
            Assert.Equal(3.250m, balance.ConsumedQuantity);
        }

        await using var invalid = _fixture.CreateContext(_fixture.TenantA);
        var invalidPeriodId = Guid.NewGuid();
        invalid.LeavePeriods.Add(new LeavePeriod { Id = invalidPeriodId, TenantId = _fixture.TenantA, Code = $"INVALID-{invalidPeriodId:N}"[..16], Name = "Invalid balance period", StartDate = new(2026, 1, 1), EndDate = new(2027, 12, 31), IsActive = true });
        invalid.EmployeeLeaveBalances.Add(new EmployeeLeaveBalance
        {
            Id = Guid.NewGuid(), TenantId = _fixture.TenantA, EmployeeId = _fixture.EmployeeA,
            LeaveTypeId = scenario.LeaveTypeId, LeavePeriodId = invalidPeriodId, GrantedQuantity = 10,
            ReservedQuantity = 8, ConsumedQuantity = 3
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Concurrent_allocated_submissions_with_insufficient_total_balance_have_one_winner()
    {
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, granted: 10, reserved: 0, consumed: 0);
        var results = await Task.WhenAll(
            SubmitAsync(scenario, _fixture.EmployeeA, "submit-six-a", 6, new(2026, 10, 10)),
            SubmitAsync(scenario, _fixture.EmployeeA, "submit-six-b", 6, new(2026, 10, 11)));

        Assert.Single(results, x => x.Succeeded);
        Assert.Single(results, x => !x.Succeeded && x.Message.Contains(LeaveBalanceAccountingErrorCodes.InsufficientLeaveBalance, StringComparison.Ordinal));
        await AssertBalanceAsync(scenario, 6, 0, 4);
        await AssertLedgerAsync(scenario, LeaveBalanceTransactionType.Reservation, 1);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Concurrent_allocated_submissions_where_both_fit_preserve_available_balance()
    {
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, granted: 10, reserved: 0, consumed: 0);
        var results = await Task.WhenAll(
            SubmitAsync(scenario, _fixture.EmployeeA, "submit-four", 4, new(2026, 10, 12)),
            SubmitAsync(scenario, _fixture.EmployeeA, "submit-five", 5, new(2026, 10, 13)));

        Assert.All(results, x => Assert.True(x.Succeeded, x.Message));
        await AssertBalanceAsync(scenario, 9, 0, 1);
        await AssertLedgerAsync(scenario, LeaveBalanceTransactionType.Reservation, 2);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Same_key_replay_and_different_payload_are_exactly_once()
    {
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, granted: 10, reserved: 0, consumed: 0);
        var concurrent = await Task.WhenAll(
            SubmitAsync(scenario, _fixture.EmployeeA, "same-key-sql", 3, new(2026, 10, 14)),
            SubmitAsync(scenario, _fixture.EmployeeA, "same-key-sql", 3, new(2026, 10, 14)));
        var conflict = await SubmitAsync(scenario, _fixture.EmployeeA, "same-key-sql", 4, new(2026, 10, 15));

        Assert.All(concurrent, x => Assert.True(x.Succeeded, x.Message));
        Assert.Equal(concurrent[0].Value!.RequestId, concurrent[1].Value!.RequestId);
        Assert.Contains(concurrent, x => x.Value!.IdempotentReplay);
        Assert.Contains(concurrent, x => !x.Value!.IdempotentReplay);
        Assert.Equal(ResultStatus.Conflict, conflict.Status);
        Assert.Contains(LeaveRequestSubmissionErrorCodes.IdempotencyConflict, conflict.Message);
        await AssertBalanceAsync(scenario, 3, 0, 7);
        await AssertLedgerAsync(scenario, LeaveBalanceTransactionType.Reservation, 1);
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        Assert.Equal(1, await db.LeaveRequests.CountAsync(x => x.TenantId == _fixture.TenantA && x.EmployeeId == _fixture.EmployeeA && x.IdempotencyKey == "same-key-sql"));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Approve_vs_approve_consumes_one_reservation()
    {
        await PrepareApprovalActorAsync();
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, LeaveRequestStatus.PendingApproval, 9, 3, 2);
        var results = await Task.WhenAll(ApproveAsync(scenario.RequestId), ApproveAsync(scenario.RequestId));

        Assert.Single(results, x => x.Succeeded);
        Assert.Single(results, x => !x.Succeeded && x.Message.Contains("InvalidStatusTransition", StringComparison.Ordinal));
        await AssertRequestAsync(scenario, LeaveRequestStatus.Approved);
        await AssertBalanceAsync(scenario, 0, 5, 4);
        await AssertLedgerAsync(scenario, LeaveBalanceTransactionType.Consumption, 1);
        await AssertEventAsync(scenario, LeaveRequestEventType.Approved, 1);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Approve_vs_reject_has_one_terminal_accounting_effect()
    {
        await PrepareApprovalActorAsync();
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, LeaveRequestStatus.PendingApproval, 9, 3, 2);
        var approve = ApproveAsync(scenario.RequestId);
        var reject = RejectAsync(scenario.RequestId);
        await Task.WhenAll(approve, reject);
        var state = await ReadAsync(scenario);

        Assert.Single(new[] { (await approve).Succeeded, (await reject).Succeeded }, x => x);
        Assert.True(state.Request.Status is LeaveRequestStatus.Approved or LeaveRequestStatus.Rejected);
        Assert.Equal(state.Request.Status == LeaveRequestStatus.Approved ? 1 : 0, state.Ledger.Count(x => x.TransactionType == LeaveBalanceTransactionType.Consumption));
        Assert.Equal(state.Request.Status == LeaveRequestStatus.Rejected ? 1 : 0, state.Ledger.Count(x => x.TransactionType == LeaveBalanceTransactionType.ReservationRelease));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Approve_vs_withdraw_has_one_terminal_accounting_effect()
    {
        await PrepareApprovalActorAsync();
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, LeaveRequestStatus.PendingApproval, 9, 3, 2);
        var approve = ApproveAsync(scenario.RequestId);
        var withdraw = WithdrawAsync(scenario.RequestId);
        await Task.WhenAll(approve, withdraw);
        var state = await ReadAsync(scenario);

        Assert.True(state.Request.Status is LeaveRequestStatus.Approved or LeaveRequestStatus.Withdrawn);
        Assert.Equal(1, state.Ledger.Count(x => x.TransactionType is LeaveBalanceTransactionType.Consumption or LeaveBalanceTransactionType.ReservationRelease));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Reject_vs_withdraw_releases_one_reservation()
    {
        await PrepareApprovalActorAsync();
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, LeaveRequestStatus.PendingApproval, 9, 3, 2);
        var reject = RejectAsync(scenario.RequestId);
        var withdraw = WithdrawAsync(scenario.RequestId);
        await Task.WhenAll(reject, withdraw);
        var state = await ReadAsync(scenario);

        Assert.True(state.Request.Status is LeaveRequestStatus.Rejected or LeaveRequestStatus.Withdrawn);
        Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.ReservationRelease);
        Assert.Equal(0m, state.Balance!.ReservedQuantity);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Cancel_vs_cancel_restores_consumption_once()
    {
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, LeaveRequestStatus.Approved, 9, 0, 5);
        var first = CancelAsync(scenario.RequestId);
        var second = CancelAsync(scenario.RequestId);
        await Task.WhenAll(first, second);

        var state = await ReadAsync(scenario);
        Assert.Single(new[] { (await first).Succeeded, (await second).Succeeded }, x => x);
        Assert.Equal(LeaveRequestStatus.Cancelled, state.Request.Status);
        Assert.Equal(2m, state.Balance!.ConsumedQuantity);
        Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.CancellationRestore);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Cancelled);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Cancel_vs_new_allocated_submission_preserves_balance_identity()
    {
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, LeaveRequestStatus.Approved, 9, 0, 5);
        var cancel = CancelAsync(scenario.RequestId);
        var submit = SubmitAsync(scenario, _fixture.EmployeeA, "cancel-submit-new", 3, new(2026, 11, 2));
        await Task.WhenAll(cancel, submit);

        var cancelResult = await cancel;
        var submitResult = await submit;
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        var balance = await db.EmployeeLeaveBalances.SingleAsync(x => x.Id == scenario.BalanceId);
        Assert.True(balance.GrantedQuantity - balance.ReservedQuantity - balance.ConsumedQuantity >= 0);
        Assert.Equal(balance.GrantedQuantity - balance.ReservedQuantity - balance.ConsumedQuantity, balance.AvailableQuantity);
        Assert.True(cancelResult.Succeeded, cancelResult.Message);
        var requestIds = new List<Guid> { scenario.RequestId };
        if (submitResult.Succeeded) requestIds.Add(submitResult.Value!.RequestId);
        Assert.Equal(1, await db.LeaveBalanceTransactions.CountAsync(x => requestIds.Contains(x.LeaveRequestId!.Value) && x.TransactionType == LeaveBalanceTransactionType.CancellationRestore));
        Assert.Equal(submitResult.Succeeded ? 1 : 0, await db.LeaveBalanceTransactions.CountAsync(x => submitResult.Succeeded && x.LeaveRequestId == submitResult.Value!.RequestId && x.TransactionType == LeaveBalanceTransactionType.Reservation));
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Different_employees_and_tenants_keep_accounting_isolated()
    {
        var a = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, granted: 9, reserved: 0, consumed: 0);
        var b = await SeedAsync(_fixture.TenantA, _fixture.EmployeeB, granted: 9, reserved: 0, consumed: 0);
        var c = await SeedAsync(_fixture.TenantB, _fixture.EmployeeC, granted: 9, reserved: 0, consumed: 0);
        var results = await Task.WhenAll(SubmitAsync(a, _fixture.EmployeeA, "isolation-a", 3, new(2026, 11, 10)), SubmitAsync(b, _fixture.EmployeeB, "isolation-b", 4, new(2026, 11, 11)), SubmitAsync(c, _fixture.EmployeeC, "isolation-c", 5, new(2026, 11, 12)));
        Assert.All(results, x => Assert.True(x.Succeeded, x.Message));
        await AssertBalanceAsync(a, 3, 0, 6);
        await AssertBalanceAsync(b, 4, 0, 5);
        await AssertBalanceAsync(c, 5, 0, 4);
    }

    [SqlServerLeaveRequestConcurrencyFact, Trait("Category", "SqlServerIntegration")]
    public async Task Allocated_lifecycle_without_authoritative_consumption_fails_safely()
    {
        var scenario = await SeedAsync(_fixture.TenantA, _fixture.EmployeeA, LeaveRequestStatus.Approved, 9, 0, 2, includeLedger: false);
        var result = await CancelAsync(scenario.RequestId);
        Assert.False(result.Succeeded);
        Assert.Contains("AllocatedConsumptionNotFound", result.Message);
        await AssertRequestAsync(scenario, LeaveRequestStatus.Approved);
    }

    private async Task PrepareApprovalActorAsync()
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA);
        var employee = await db.Employees.SingleAsync(x => x.Id == _fixture.EmployeeA);
        employee.ReportingManagerId = _fixture.EmployeeB;
        var history = await db.EmployeeEmploymentHistory.SingleAsync(x => x.Id == _fixture.EmploymentA);
        history.ManagerId = _fixture.EmployeeB;
        if (!await db.Roles.AnyAsync(x => x.Id == 990001)) db.Roles.Add(new Role { Id = 990001, Name = "Allocated SQL Approver" });
        if (!await db.Permissions.AnyAsync(x => x.Id == 35)) db.Permissions.Add(new Permission { Id = 35, Name = Permissions.Leave.Approve });
        if (!await db.RolePermissions.AnyAsync(x => x.RoleId == 990001 && x.PermissionId == 35)) db.RolePermissions.Add(new RolePermission { RoleId = 990001, PermissionId = 35 });
        if (!await db.UserRoles.AnyAsync(x => x.TenantId == _fixture.TenantA && x.UserId == _fixture.UserB && x.RoleId == 990001)) db.UserRoles.Add(new UserRole { TenantId = _fixture.TenantA, UserId = _fixture.UserB, RoleId = 990001 });
        await db.SaveChangesAsync();
    }

    private async Task<Scenario> SeedAsync(Guid tenantId, Guid employeeId, LeaveRequestStatus? status = null, decimal granted = 9, decimal reserved = 0, decimal consumed = 0, bool includeLedger = true)
    {
        var employmentId = employeeId == _fixture.EmployeeA ? _fixture.EmploymentA : employeeId == _fixture.EmployeeB ? _fixture.EmploymentB : _fixture.EmploymentC;
        var scenario = new Scenario(tenantId, employeeId, employmentId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await using var db = _fixture.CreateContext(tenantId);
        db.AddRange(
            new LeaveType { Id = scenario.LeaveTypeId, TenantId = tenantId, Code = $"ALLOC-{scenario.LeaveTypeId:N}"[..12], Name = "Allocated SQL Leave", DefaultUnit = LeaveUnit.Day, IsActive = true },
            new LeavePeriod { Id = scenario.LeavePeriodId, TenantId = tenantId, Code = $"P-{scenario.LeavePeriodId:N}"[..12], Name = "Allocated SQL Period", StartDate = new(2026, 1, 1), EndDate = new(2027, 12, 31), IsActive = true },
            new LeavePolicy { Id = scenario.PolicyId, TenantId = tenantId, Code = $"POL-{scenario.PolicyId:N}"[..12], Name = "Allocated SQL Policy", IsActive = true },
            new LeavePolicyVersion { Id = scenario.PolicyVersionId, TenantId = tenantId, LeavePolicyId = scenario.PolicyId, VersionNumber = 1, EffectiveFrom = new(2026, 1, 1), Status = LeavePolicyVersionStatus.Published, Priority = 1 },
            new LeavePolicyRule { Id = scenario.PolicyRuleId, TenantId = tenantId, LeavePolicyVersionId = scenario.PolicyVersionId, LeaveTypeId = scenario.LeaveTypeId, IsActive = true },
            new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyRuleId = scenario.PolicyRuleId, EntitlementMode = EntitlementMode.Allocated },
            new LeavePolicyCancellationRule { Id = Guid.NewGuid(), TenantId = tenantId, LeavePolicyRuleId = scenario.PolicyRuleId, CancelAllowed = true },
            new EmployeeLeaveBalance { Id = scenario.BalanceId, TenantId = tenantId, EmployeeId = employeeId, LeaveTypeId = scenario.LeaveTypeId, LeavePeriodId = scenario.LeavePeriodId, GrantedQuantity = granted, ReservedQuantity = reserved, ConsumedQuantity = consumed });
        if (status is LeaveRequestStatus requestStatus)
        {
            scenario.RequestId = Guid.NewGuid();
            db.LeaveRequests.Add(new LeaveRequest { Id = scenario.RequestId, TenantId = tenantId, EmployeeId = employeeId, LeaveTypeId = scenario.LeaveTypeId, LeavePeriodId = scenario.LeavePeriodId, LeavePolicyVersionId = scenario.PolicyVersionId, LeavePolicyRuleId = scenario.PolicyRuleId, EmployeeEmploymentHistoryId = scenario.EmploymentId, StartDate = new(2026, 10, 1), EndDate = new(2026, 10, 1), RequestedQuantity = 3, ChargeableQuantity = 3, Status = requestStatus, SubmittedAtUtc = DateTime.UtcNow, IdempotencyKey = $"seed-{scenario.RequestId:N}", PayloadFingerprint = new string('a', 64) });
        }
        await db.SaveChangesAsync();
        if (status is LeaveRequestStatus requestStatus2)
        {
            db.LeaveRequestEvents.Add(new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = scenario.RequestId, EventType = LeaveRequestEventType.Submitted, OccurredAtUtc = DateTime.UtcNow, ActorType = LeaveBalanceActorType.User, ActorUserId = tenantId == _fixture.TenantA ? _fixture.UserA : _fixture.UserC, ActorEmployeeId = employeeId });
            if (requestStatus2 == LeaveRequestStatus.Approved) db.LeaveRequestEvents.Add(new LeaveRequestEvent { Id = Guid.NewGuid(), TenantId = tenantId, LeaveRequestId = scenario.RequestId, EventType = LeaveRequestEventType.Approved, OccurredAtUtc = DateTime.UtcNow, ActorType = LeaveBalanceActorType.User, ActorUserId = tenantId == _fixture.TenantA ? _fixture.UserA : _fixture.UserC, ActorEmployeeId = employeeId });
            if (includeLedger)
            {
                var type = requestStatus2 == LeaveRequestStatus.Approved ? LeaveBalanceTransactionType.Consumption : LeaveBalanceTransactionType.Reservation;
                db.LeaveBalanceTransactions.Add(new LeaveBalanceTransaction { Id = Guid.NewGuid(), TenantId = tenantId, EmployeeLeaveBalanceId = scenario.BalanceId, EmployeeId = employeeId, LeaveTypeId = scenario.LeaveTypeId, LeavePeriodId = scenario.LeavePeriodId, LeaveRequestId = scenario.RequestId, TransactionType = type, Quantity = 3, EffectiveDate = new(2026, 10, 1), OccurredAtUtc = DateTime.UtcNow, LeavePolicyVersionId = scenario.PolicyVersionId, LeavePolicyRuleId = scenario.PolicyRuleId, SourceType = LeaveBalanceSourceType.Policy, SourceReference = $"LeaveRequest:{scenario.RequestId:D}", ActorType = LeaveBalanceActorType.User, ActorUserId = tenantId == _fixture.TenantA ? _fixture.UserA : _fixture.UserC, ActorEmployeeId = employeeId, IdempotencyKey = $"seed-ledger-{scenario.RequestId:N}", PayloadFingerprint = new string('b', 64) });
            }
            await db.SaveChangesAsync();
        }
        return scenario;
    }

    private Task<Result<LeaveRequestSubmissionResult>> SubmitAsync(Scenario scenario, Guid employeeId, string key, decimal quantity, DateOnly? date = null)
    {
        var tenant = scenario.TenantId; var user = tenant == _fixture.TenantA ? (employeeId == _fixture.EmployeeB ? _fixture.UserB : _fixture.UserA) : _fixture.UserC;
        return Task.Run(async () =>
        {
            await using var db = _fixture.CreateContext(tenant, user);
            var context = new TestTenantContext(tenant, user);
            var input = new LeaveRequestSubmissionInput(scenario.LeaveTypeId, date ?? new(2026, 10, 2), date ?? new(2026, 10, 2), key);
            var validation = new FixedValidation(scenario, employeeId, quantity, input);
            return await new LeaveRequestSubmissionService(db, new FixedIdentity(tenant, user, employeeId), validation, new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System, retryPolicy: RetryPolicy(), balanceAccountingService: new LeaveBalanceAccountingService(db, context, TimeProvider.System)).SubmitAsync(input);
        });
    }

    private Task<Result<LeaveRequestApprovalResult>> ApproveAsync(Guid requestId) => ApprovalAsync(requestId, true);
    private Task<Result<LeaveRequestApprovalResult>> RejectAsync(Guid requestId) => ApprovalAsync(requestId, false);
    private async Task<Result<LeaveRequestApprovalResult>> ApprovalAsync(Guid requestId, bool approve)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, _fixture.UserB);
        var tenant = new TestTenantContext(_fixture.TenantA, _fixture.UserB);
        var service = new LeaveRequestApprovalService(db, new FixedIdentity(_fixture.TenantA, _fixture.UserB, _fixture.EmployeeB), new EmployeeManagerResolver(db, tenant), new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System, retryPolicy: RetryPolicy(), balanceAccountingService: new LeaveBalanceAccountingService(db, tenant, TimeProvider.System));
        return approve ? await service.ApproveAsync(requestId) : await service.RejectAsync(requestId);
    }

    private async Task<Result<LeaveRequestWithdrawalResult>> WithdrawAsync(Guid requestId)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, _fixture.UserA);
        var tenant = new TestTenantContext(_fixture.TenantA, _fixture.UserA);
        return await new LeaveRequestWithdrawalService(db, new FixedIdentity(_fixture.TenantA, _fixture.UserA, _fixture.EmployeeA), new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System, retryPolicy: RetryPolicy(), balanceAccountingService: new LeaveBalanceAccountingService(db, tenant, TimeProvider.System)).WithdrawAsync(requestId);
    }

    private async Task<Result<LeaveRequestCancellationResult>> CancelAsync(Guid requestId)
    {
        await using var db = _fixture.CreateContext(_fixture.TenantA, _fixture.UserA);
        var tenant = new TestTenantContext(_fixture.TenantA, _fixture.UserA);
        return await new LeaveRequestCancellationService(db, new FixedIdentity(_fixture.TenantA, _fixture.UserA, _fixture.EmployeeA), new SqlServerLeaveRequestSubmissionLock(db), TimeProvider.System, retryPolicy: RetryPolicy(), balanceAccountingService: new LeaveBalanceAccountingService(db, tenant, TimeProvider.System)).CancelAsync(requestId);
    }

    private LeaveRequestSubmissionRetryPolicy RetryPolicy() => new(new SqlServerLeaveRequestSubmissionDeadlockClassifier(), NullLogger<LeaveRequestSubmissionRetryPolicy>.Instance);

    private async Task<State> ReadAsync(Scenario scenario)
    {
        await using var db = _fixture.CreateContext(scenario.TenantId);
        return new State(await db.LeaveRequests.SingleAsync(x => x.Id == scenario.RequestId), await db.EmployeeLeaveBalances.SingleAsync(x => x.Id == scenario.BalanceId), await db.LeaveBalanceTransactions.Where(x => x.LeaveRequestId == scenario.RequestId).ToListAsync(), await db.LeaveRequestEvents.Where(x => x.LeaveRequestId == scenario.RequestId).ToListAsync());
    }

    private async Task AssertRequestAsync(Scenario scenario, LeaveRequestStatus status) => Assert.Equal(status, (await ReadAsync(scenario)).Request.Status);
    private async Task AssertBalanceAsync(Scenario scenario, decimal reserved, decimal consumed, decimal available)
    {
        await using var db = _fixture.CreateContext(scenario.TenantId);
        var balance = await db.EmployeeLeaveBalances.SingleAsync(x => x.Id == scenario.BalanceId);
        Assert.Equal(reserved, balance.ReservedQuantity); Assert.Equal(consumed, balance.ConsumedQuantity); Assert.Equal(available, balance.AvailableQuantity);
    }
    private async Task AssertLedgerAsync(Scenario scenario, LeaveBalanceTransactionType type, int count) { await using var db = _fixture.CreateContext(scenario.TenantId); Assert.Equal(count, await db.LeaveBalanceTransactions.CountAsync(x => x.TenantId == scenario.TenantId && x.EmployeeId == scenario.EmployeeId && x.LeaveTypeId == scenario.LeaveTypeId && x.LeavePeriodId == scenario.LeavePeriodId && x.TransactionType == type)); }
    private async Task AssertEventAsync(Scenario scenario, LeaveRequestEventType type, int count) { await using var db = _fixture.CreateContext(scenario.TenantId); Assert.Equal(count, await db.LeaveRequestEvents.CountAsync(x => x.LeaveRequestId == scenario.RequestId && x.EventType == type)); }

    private sealed class FixedIdentity(Guid tenant, Guid user, Guid employee) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenant, user, employee)));
    }

    private sealed class FixedValidation(Scenario scenario, Guid employeeId, decimal quantity, LeaveRequestSubmissionInput input) : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(LeaveRequestValidationInput request, CancellationToken cancellationToken = default) => Task.FromResult(Result<LeaveRequestValidationResult>.Success(new(employeeId, scenario.LeaveTypeId, scenario.EmploymentId, scenario.LeavePeriodId, scenario.PolicyVersionId, scenario.PolicyRuleId, Gender.Unspecified, input.StartDate, input.EndDate, quantity, quantity, [new LeaveRequestDayValidationResult(input.StartDate, quantity, quantity, null, null, true)], EntitlementMode.Allocated, true, false, input.IdempotencyKey, Fingerprint(input), 1, 1)));
        private static string Fingerprint(LeaveRequestSubmissionInput value) => $"{value.LeaveTypeId:N}:{value.StartDate:yyyyMMdd}:{value.EndDate:yyyyMMdd}".PadRight(64, '0');
    }

    private sealed record Scenario(Guid TenantId, Guid EmployeeId, Guid EmploymentId, Guid LeaveTypeId, Guid LeavePeriodId, Guid PolicyId, Guid PolicyVersionId, Guid PolicyRuleId, Guid BalanceId)
    {
        public Guid RequestId { get; set; }
    }

    private sealed record State(LeaveRequest Request, EmployeeLeaveBalance Balance, IReadOnlyList<LeaveBalanceTransaction> Ledger, IReadOnlyList<LeaveRequestEvent> Events);
}
