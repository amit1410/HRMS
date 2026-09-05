using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.DTOs.Leave;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Infrastructure.Persistence;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace HRMS.Tests;

public sealed class LeaveRequestAllocatedSubmissionTests
{
    [Fact]
    public async Task Allocated_submission_reserves_authoritative_chargeable_quantity_atomically()
    {
        // SQLite maps this decimal model to TEXT in the in-memory schema; use single-digit grants here to avoid TEXT-affinity comparison behavior. Production SQL Server decimal/check semantics are validated separately.
        using var fixture = await SubmissionFixture.CreateAsync(granted: 9m, reserved: 2m, consumed: 1m);

        var result = await fixture.SubmitAsync();

        Assert.True(result.Succeeded, $"Status={result.Status}; Message={result.Message}");
        Assert.Equal(LeaveRequestStatus.PendingApproval, result.Value!.Status);
        Assert.Equal(3m, result.Value.ChargeableQuantity);
        var state = await fixture.ReadAsync();
        Assert.Equal(9m, state.Balance.GrantedQuantity);
        Assert.Equal(5m, state.Balance.ReservedQuantity);
        Assert.Equal(1m, state.Balance.ConsumedQuantity);
        Assert.Equal(3m, state.Balance.AvailableQuantity);
        var reservation = Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Reservation);
        Assert.Equal(3m, reservation.Quantity);
        Assert.Equal(state.Request.Id, reservation.LeaveRequestId);
        Assert.Equal(fixture.LeavePeriodId, reservation.LeavePeriodId);
        Assert.Equal(fixture.PolicyVersionId, reservation.LeavePolicyVersionId);
        Assert.Equal(fixture.PolicyRuleId, reservation.LeavePolicyRuleId);
        Assert.Single(state.Request.Events, x => x.EventType == LeaveRequestEventType.Submitted);
    }

    [Fact]
    public async Task Missing_balance_writes_no_request_or_reservation()
    {
        using var fixture = await SubmissionFixture.CreateAsync(granted: null);

        var result = await fixture.SubmitAsync();

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Contains("BalanceNotInitialized", result.Message);
        var state = await fixture.ReadAsync();
        Assert.Null(state.Request);
        Assert.Empty(state.Days);
        Assert.Empty(state.Events);
        Assert.Empty(state.Ledger);
    }

    [Fact]
    public async Task Insufficient_balance_writes_no_request_or_reservation()
    {
        using var fixture = await SubmissionFixture.CreateAsync(granted: 5m, reserved: 3m, consumed: 1m);

        var result = await fixture.SubmitAsync();

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("InsufficientLeaveBalance", result.Message);
        var state = await fixture.ReadAsync();
        Assert.Equal(3m, state.Balance.ReservedQuantity);
        Assert.Equal(1m, state.Balance.ConsumedQuantity);
        Assert.Null(state.Request);
        Assert.Empty(state.Days);
        Assert.Empty(state.Events);
        Assert.Empty(state.Ledger);
    }

    [Fact]
    public async Task Exact_balance_submission_succeeds_and_leaves_zero_available()
    {
        using var fixture = await SubmissionFixture.CreateAsync(granted: 3m);

        var result = await fixture.SubmitAsync();

        Assert.True(result.Succeeded);
        var state = await fixture.ReadAsync();
        Assert.Equal(0m, state.Balance.AvailableQuantity);
        Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Reservation);
    }

    [Fact]
    public async Task Same_allocated_submission_replays_without_a_second_reservation()
    {
        // SQLite maps this decimal model to TEXT in the in-memory schema; use a single-digit grant here. Production SQL Server decimal/check semantics are validated separately.
        using var fixture = await SubmissionFixture.CreateAsync(granted: 9m);

        var first = await fixture.SubmitAsync();
        var replay = await fixture.SubmitAsync();

        Assert.True(first.Succeeded, $"FIRST SUBMISSION FAILED. Status={first.Status}; Message={first.Message}; Persistence={fixture.PersistenceException(0)}; SQLite={fixture.LastSqliteDiagnostics}");
        Assert.True(replay.Succeeded, $"IDEMPOTENT REPLAY FAILED. Status={replay.Status}; Message={replay.Message}; Persistence={fixture.PersistenceException(1)}; SQLite={fixture.LastSqliteDiagnostics}");
        Assert.Equal(first.Value!.RequestId, replay.Value!.RequestId);
        Assert.True(replay.Value.IdempotentReplay);
        var state = await fixture.ReadAsync();
        Assert.Single(state.Requests);
        Assert.Single(state.Events, x => x.EventType == LeaveRequestEventType.Submitted);
        Assert.Single(state.Ledger, x => x.TransactionType == LeaveBalanceTransactionType.Reservation);
        Assert.Equal(9m, state.Balance.GrantedQuantity);
        Assert.Equal(3m, state.Balance.ReservedQuantity);
        Assert.Equal(0m, state.Balance.ConsumedQuantity);
        Assert.Equal(6m, state.Balance.AvailableQuantity);
    }

    [Fact]
    public async Task Unlimited_submission_does_not_require_or_mutate_balance()
    {
        using var fixture = await SubmissionFixture.CreateAsync(granted: null, mode: EntitlementMode.Unlimited);

        var result = await fixture.SubmitAsync();

        Assert.True(result.Succeeded);
        var state = await fixture.ReadAsync();
        Assert.Null(state.Balance);
        Assert.Empty(state.Ledger);
    }

    [Fact]
    public async Task No_balance_required_submission_does_not_require_or_mutate_balance()
    {
        using var fixture = await SubmissionFixture.CreateAsync(granted: null, mode: EntitlementMode.NoBalanceRequired);

        var result = await fixture.SubmitAsync();

        Assert.True(result.Succeeded);
        var state = await fixture.ReadAsync();
        Assert.Null(state.Balance);
        Assert.Empty(state.Ledger);
    }

    private sealed class FixedIdentity(Guid tenantId, Guid userId, Guid employeeId) : IEmployeeIdentityResolver
    {
        public Task<Result<RuntimeEmployeeIdentity>> ResolveCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<RuntimeEmployeeIdentity>.Success(new(tenantId, userId, employeeId)));
    }

    private sealed class FixedValidation(LeaveRequestValidationResult result) : ILeaveRequestValidationService
    {
        public Task<Result<LeaveRequestValidationResult>> ValidateAsync(
            LeaveRequestValidationInput input,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<LeaveRequestValidationResult>.Success(result));
    }

    private sealed class NoOpLock : ILeaveRequestSubmissionLock
    {
        public Task AcquireAsync(Guid tenantId, Guid employeeId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SubmissionFixture : IDisposable
    {
        public readonly Guid TenantId = Guid.NewGuid();
        public readonly Guid UserId = Guid.NewGuid();
        public readonly Guid EmployeeId = Guid.NewGuid();
        public readonly Guid LeaveTypeId = Guid.NewGuid();
        public readonly Guid LeavePeriodId = Guid.NewGuid();
        public readonly Guid PolicyId = Guid.NewGuid();
        public readonly Guid PolicyVersionId = Guid.NewGuid();
        public readonly Guid PolicyRuleId = Guid.NewGuid();
        public readonly Guid EmploymentId = Guid.NewGuid();
        private readonly Guid _balanceId = Guid.NewGuid();
        private readonly SqliteInMemoryDatabase _database;
        private readonly EntitlementMode _mode;

        private SubmissionFixture(SqliteInMemoryDatabase database, EntitlementMode mode) => (_database, _mode) = (database, mode);

        public static async Task<SubmissionFixture> CreateAsync(
            decimal? granted,
            decimal reserved = 0m,
            decimal consumed = 0m,
            EntitlementMode mode = EntitlementMode.Allocated)
        {
            var fixture = new SubmissionFixture(new SqliteInMemoryDatabase(), mode);
            await fixture.SeedAsync(granted, reserved, consumed);
            return fixture;
        }

        public async Task<Result<LeaveRequestSubmissionResult>> SubmitAsync()
        {
            if (_mode == EntitlementMode.Allocated)
                LastSqliteDiagnostics = await ReadSqliteDiagnosticsAsync();
            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            var validation = new FixedValidation(new(
                EmployeeId, LeaveTypeId, EmploymentId, LeavePeriodId, PolicyVersionId, PolicyRuleId,
                Gender.Unspecified, new(2027, 1, 2), new(2027, 1, 2), 9m, 3m,
                [new(new(2027, 1, 2), 9m, 3m, "WorkingDay", "test", true)], _mode,
                _mode == EntitlementMode.Allocated, false, "allocated-key", new string('a', 64), 1, 1));
            var accounting = new LeaveBalanceAccountingService(context, new TestTenantContext(TenantId), TimeProvider.System);
            var service = new LeaveRequestSubmissionService(
                context,
                new FixedIdentity(TenantId, UserId, EmployeeId),
                validation,
                new NoOpLock(),
                TimeProvider.System,
                diagnosticObserver: exception => _persistenceExceptions.Add(exception),
                balanceAccountingService: accounting);
            return await service.SubmitAsync(new(LeaveTypeId, new(2027, 1, 2), new(2027, 1, 2), "allocated-key"));
        }

        private readonly List<Exception> _persistenceExceptions = [];
        public string? LastSqliteDiagnostics { get; private set; }

        public string PersistenceException(int index) => index < _persistenceExceptions.Count
            ? string.Join(" -> ", ExceptionChain(_persistenceExceptions[index]))
            : "none captured";

        private static IEnumerable<string> ExceptionChain(Exception exception)
        {
            for (Exception? current = exception; current is not null; current = current.InnerException)
                yield return $"{current.GetType().FullName}: {current.Message}";
        }

        private async Task<string> ReadSqliteDiagnosticsAsync()
        {
            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            try
            {
                string values;
                await using (var valueCommand = context.Database.GetDbConnection().CreateCommand())
                {
                    valueCommand.CommandText = """
                        SELECT
                            typeof(GrantedQuantity), quote(GrantedQuantity),
                            typeof(ReservedQuantity), quote(ReservedQuantity),
                            typeof(ConsumedQuantity), quote(ConsumedQuantity),
                            ReservedQuantity + ConsumedQuantity,
                            typeof(ReservedQuantity + ConsumedQuantity),
                            ReservedQuantity + ConsumedQuantity <= GrantedQuantity,
                            3 <= GrantedQuantity,
                            CAST(3 AS NUMERIC) <= CAST(GrantedQuantity AS NUMERIC)
                        FROM EmployeeLeaveBalances
                        LIMIT 1;
                        """;
                    await using var reader = await valueCommand.ExecuteReaderAsync();
                    if (!await reader.ReadAsync())
                        return "No balance row found";
                    values = string.Join(", ", Enumerable.Range(0, reader.FieldCount).Select(index =>
                        $"{index}={reader.GetValue(index)}"));
                }

                string? createTable;
                await using (var schemaCommand = context.Database.GetDbConnection().CreateCommand())
                {
                    schemaCommand.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'EmployeeLeaveBalances';";
                    createTable = (string?)await schemaCommand.ExecuteScalarAsync();
                }
                return $"Values=[{values}], CreateTable={createTable}";
            }
            catch (Exception diagnosticException)
            {
                return $"SQLite diagnostic collection failed: {diagnosticException.GetType().FullName}: {diagnosticException.Message}";
            }
        }

        public async Task<State> ReadAsync()
        {
            await using var context = _database.CreateContext(new TestTenantContext(TenantId));
            return new State(
                await context.LeaveRequests.Include(x => x.Events).SingleOrDefaultAsync(),
                await context.LeaveRequests.ToListAsync(),
                await context.LeaveRequestDays.ToListAsync(),
                await context.LeaveRequestEvents.ToListAsync(),
                await context.EmployeeLeaveBalances.SingleOrDefaultAsync(),
                await context.LeaveBalanceTransactions.ToListAsync());
        }

        private async Task SeedAsync(decimal? granted, decimal reserved, decimal consumed)
        {
            await using var context = _database.CreateContext(new TestTenantContext());
            context.AddRange(
                new Tenant { Id = TenantId, TenantCode = TenantId.ToString("N")[..8], Host = $"{TenantId}.local", ShardKey = TenantId.ToString("N"), TenantName = "Test" },
                new User { Id = UserId, TenantId = TenantId, Email = $"{UserId}@test.local", PasswordHash = "test", FirstName = "Test", LastName = "User" },
                new Employee { Id = EmployeeId, TenantId = TenantId, EmployeeCode = "EMP-1", FirstName = "Test", LastName = "Employee", Email = $"employee-{EmployeeId}@test.local" },
                new LeaveType { Id = LeaveTypeId, TenantId = TenantId, Code = "AL", Name = "Annual Leave" },
                new LeavePeriod { Id = LeavePeriodId, TenantId = TenantId, Code = "2027", Name = "2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) },
                new LeavePolicy { Id = PolicyId, TenantId = TenantId, Code = "POL", Name = "Policy" },
                new LeavePolicyVersion { Id = PolicyVersionId, TenantId = TenantId, LeavePolicyId = PolicyId, VersionNumber = 1, EffectiveFrom = new(2027, 1, 1), Status = LeavePolicyVersionStatus.Published },
                new LeavePolicyRule { Id = PolicyRuleId, TenantId = TenantId, LeavePolicyVersionId = PolicyVersionId, LeaveTypeId = LeaveTypeId },
                new LeavePolicyEntitlementRule { Id = Guid.NewGuid(), TenantId = TenantId, LeavePolicyRuleId = PolicyRuleId, EntitlementMode = _mode },
                new EmployeeEmploymentHistory { Id = EmploymentId, TenantId = TenantId, EmployeeId = EmployeeId, EffectiveFrom = new(2020, 1, 1) });
            await context.SaveChangesAsync();
            if (granted is decimal value)
            {
                var balance = new EmployeeLeaveBalance
                {
                    Id = _balanceId,
                    TenantId = TenantId,
                    EmployeeId = EmployeeId,
                    LeaveTypeId = LeaveTypeId,
                    LeavePeriodId = LeavePeriodId,
                    GrantedQuantity = value,
                    ReservedQuantity = reserved,
                    ConsumedQuantity = consumed
                };
                context.EmployeeLeaveBalances.Add(balance);
                var trackedBalances = context.ChangeTracker.Entries<EmployeeLeaveBalance>().ToArray();
                Assert.Single(trackedBalances);
                Assert.Equal(EntityState.Added, trackedBalances[0].State);
                Assert.Equal(granted.Value, balance.GrantedQuantity);
                Assert.Equal(reserved, balance.ReservedQuantity);
                Assert.Equal(consumed, balance.ConsumedQuantity);
                Assert.Equal(granted.Value - reserved - consumed, balance.AvailableQuantity);
                Assert.True(balance.ReservedQuantity + balance.ConsumedQuantity <= balance.GrantedQuantity);
                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateException exception)
                {
                    await using var command = context.Database.GetDbConnection().CreateCommand();
                    command.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'EmployeeLeaveBalances';";
                    var createTable = (string?)await command.ExecuteScalarAsync();
                    throw new InvalidOperationException(
                        $"EmployeeLeaveBalances seed failed. CreateTable={createTable}", exception);
                }
            }
        }

        public void Dispose() => _database.Dispose();
    }

    private sealed record State(
        LeaveRequest? Request,
        IReadOnlyList<LeaveRequest> Requests,
        IReadOnlyList<LeaveRequestDay> Days,
        IReadOnlyList<LeaveRequestEvent> Events,
        EmployeeLeaveBalance? Balance,
        IReadOnlyList<LeaveBalanceTransaction> Ledger);
}
