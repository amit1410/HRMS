using HRMS.Application.Abstractions;
using HRMS.Application.Common;
using HRMS.Application.Services;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using HRMS.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HRMS.Tests;

public sealed class LeaveBalanceAccountingFoundationTests
{
    [Fact]
    public void Transaction_type_values_are_stable()
    {
        Assert.Equal(0, (int)LeaveBalanceTransactionType.Opening);
        Assert.Equal(1, (int)LeaveBalanceTransactionType.Accrual);
        Assert.Equal(2, (int)LeaveBalanceTransactionType.ExternalGrant);
        Assert.Equal(3, (int)LeaveBalanceTransactionType.Reservation);
        Assert.Equal(4, (int)LeaveBalanceTransactionType.ReservationRelease);
        Assert.Equal(5, (int)LeaveBalanceTransactionType.Consumption);
        Assert.Equal(6, (int)LeaveBalanceTransactionType.CancellationRestore);
    }

    [Fact]
    public void Ledger_has_nullable_request_fk_restrictive_delete_and_filtered_unique_index()
    {
        using var database = new SqliteInMemoryDatabase();
        using var context = database.CreateContext(new TestTenantContext(Guid.NewGuid()));
        var ledger = context.Model.FindEntityType(typeof(LeaveBalanceTransaction))!;

        var requestProperty = ledger.FindProperty(nameof(LeaveBalanceTransaction.LeaveRequestId));
        Assert.NotNull(requestProperty);
        Assert.True(requestProperty!.IsNullable);
        Assert.Contains(ledger.GetForeignKeys(), fk =>
            fk.Properties.Select(x => x.Name).SequenceEqual([nameof(LeaveBalanceTransaction.TenantId), nameof(LeaveBalanceTransaction.LeaveRequestId)]) &&
            fk.PrincipalEntityType.ClrType == typeof(LeaveRequest) &&
            fk.DeleteBehavior == DeleteBehavior.Restrict);
        Assert.Contains(ledger.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual([
                nameof(LeaveBalanceTransaction.TenantId),
                nameof(LeaveBalanceTransaction.LeaveRequestId),
                nameof(LeaveBalanceTransaction.TransactionType)]) &&
            index.GetFilter() == "[LeaveRequestId] IS NOT NULL");
    }

    [Fact]
    public async Task Reserve_increases_reserved_and_appends_request_linked_ledger()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, granted: 5m);
        using var context = database.CreateContext(new TestTenantContext(ids.Tenant));
        var service = new LeaveBalanceAccountingService(context, new TestTenantContext(ids.Tenant), TimeProvider.System);

        var result = await service.ReserveAsync(Command(ids, 2m));

        Assert.True(result.Succeeded);
        var balance = await context.EmployeeLeaveBalances.SingleAsync();
        Assert.Equal(2m, balance.ReservedQuantity);
        Assert.Equal(0m, balance.ConsumedQuantity);
        Assert.Equal(3m, balance.AvailableQuantity);
        var ledger = await context.LeaveBalanceTransactions.SingleAsync();
        Assert.Equal(LeaveBalanceTransactionType.Reservation, ledger.TransactionType);
        Assert.Equal(ids.Request, ledger.LeaveRequestId);
        Assert.Equal(2m, ledger.Quantity);
        Assert.Equal($"{ids.Tenant:D}:leave-request:{ids.Request:D}:Reservation", ledger.IdempotencyKey);
    }

    [Fact]
    public async Task Reserve_rejects_insufficient_balance_without_mutation()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, granted: 1m);
        using var context = database.CreateContext(new TestTenantContext(ids.Tenant));
        var service = new LeaveBalanceAccountingService(context, new TestTenantContext(ids.Tenant), TimeProvider.System);

        var result = await service.ReserveAsync(Command(ids, 2m));

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("InsufficientLeaveBalance", result.Message);
        var balance = await context.EmployeeLeaveBalances.SingleAsync();
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Empty(await context.LeaveBalanceTransactions.ToListAsync());
    }

    [Fact]
    public async Task Missing_balance_returns_stable_error_without_ledger()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, granted: null);
        using var context = database.CreateContext(new TestTenantContext(ids.Tenant));
        var service = new LeaveBalanceAccountingService(context, new TestTenantContext(ids.Tenant), TimeProvider.System);

        var result = await service.ReserveAsync(Command(ids, 1m));

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Contains("BalanceNotInitialized", result.Message);
        Assert.Empty(await context.LeaveBalanceTransactions.ToListAsync());
    }

    [Fact]
    public async Task Consume_release_and_restore_apply_only_their_own_invariants()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, granted: 5m, reserved: 2m, consumed: 1m);
        using var context = database.CreateContext(new TestTenantContext(ids.Tenant));
        var service = new LeaveBalanceAccountingService(context, new TestTenantContext(ids.Tenant), TimeProvider.System);

        var consumed = await service.ConsumeReservationAsync(Command(ids, 1m));
        var released = await service.ReleaseReservationAsync(Command(ids, 1m));
        var restored = await service.RestoreConsumptionAsync(Command(ids, 0.5m));

        Assert.True(consumed.Succeeded);
        Assert.True(released.Succeeded);
        Assert.True(restored.Succeeded);
        var balance = await context.EmployeeLeaveBalances.SingleAsync();
        Assert.Equal(0m, balance.ReservedQuantity);
        Assert.Equal(1.5m, balance.ConsumedQuantity);
        Assert.Equal(3.5m, balance.AvailableQuantity);
        Assert.Equal([
            LeaveBalanceTransactionType.Consumption,
            LeaveBalanceTransactionType.ReservationRelease,
            LeaveBalanceTransactionType.CancellationRestore],
            (await context.LeaveBalanceTransactions.OrderBy(x => x.CreatedDate).ToListAsync()).Select(x => x.TransactionType));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Every_accounting_operation_rejects_non_positive_quantity(decimal quantity)
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, granted: 5m, reserved: 2m, consumed: 1m);
        using var context = database.CreateContext(new TestTenantContext(ids.Tenant));
        var service = new LeaveBalanceAccountingService(context, new TestTenantContext(ids.Tenant), TimeProvider.System);
        var command = Command(ids, quantity);

        Assert.Equal(ResultStatus.ValidationFailed, (await service.ReserveAsync(command)).Status);
        Assert.Equal(ResultStatus.ValidationFailed, (await service.ConsumeReservationAsync(command)).Status);
        Assert.Equal(ResultStatus.ValidationFailed, (await service.ReleaseReservationAsync(command)).Status);
        Assert.Equal(ResultStatus.ValidationFailed, (await service.RestoreConsumptionAsync(command)).Status);
        Assert.Empty(await context.LeaveBalanceTransactions.ToListAsync());
    }

    [Fact]
    public async Task Replaying_same_operation_is_idempotent_and_does_not_double_reserve()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, granted: 5m);
        using var context = database.CreateContext(new TestTenantContext(ids.Tenant));
        var service = new LeaveBalanceAccountingService(context, new TestTenantContext(ids.Tenant), TimeProvider.System);
        var command = Command(ids, 2m);

        var first = await service.ReserveAsync(command);
        var replay = await service.ReserveAsync(command);

        Assert.True(first.Succeeded);
        Assert.True(replay.Succeeded);
        Assert.True(replay.Value!.IsReplay);
        Assert.Equal(first.Value!.TransactionId, replay.Value.TransactionId);
        Assert.Equal(2m, (await context.EmployeeLeaveBalances.SingleAsync()).ReservedQuantity);
        Assert.Single(await context.LeaveBalanceTransactions.ToListAsync());
    }

    [Fact]
    public async Task Accounting_save_participates_in_callers_transaction()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, granted: 5m);
        using var context = database.CreateContext(new TestTenantContext(ids.Tenant));
        var service = new LeaveBalanceAccountingService(context, new TestTenantContext(ids.Tenant), TimeProvider.System);
        await using var transaction = await context.BeginTransactionAsync();

        var result = await service.ReserveAsync(Command(ids, 2m));
        Assert.True(result.Succeeded);
        await transaction.RollbackAsync();

        using var verification = database.CreateContext(new TestTenantContext(ids.Tenant));
        Assert.Equal(0m, (await verification.EmployeeLeaveBalances.SingleAsync()).ReservedQuantity);
        Assert.Empty(await verification.LeaveBalanceTransactions.ToListAsync());
    }

    [Fact]
    public async Task Request_lifecycle_unique_index_rejects_duplicate_operation_type()
    {
        using var database = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(database, granted: 5m);
        using var context = database.CreateContext(new TestTenantContext(ids.Tenant));
        var balance = await context.EmployeeLeaveBalances.SingleAsync();
        context.LeaveBalanceTransactions.AddRange(
            Ledger(ids, balance.Id, LeaveBalanceTransactionType.Reservation, "one"),
            Ledger(ids, balance.Id, LeaveBalanceTransactionType.Reservation, "two"));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private static LeaveBalanceAccountingCommand Command(Ids ids, decimal quantity) =>
        new(ids.Tenant, ids.Request, ids.Employee, ids.LeaveType, ids.LeavePeriod, ids.PolicyVersion, ids.PolicyRule,
            quantity, new(2027, 1, 1), LeaveBalanceActorType.System, null, null, null);

    private static LeaveBalanceTransaction Ledger(Ids ids, Guid balanceId, LeaveBalanceTransactionType type, string suffix) => new()
    {
        Id = Guid.NewGuid(), TenantId = ids.Tenant, EmployeeLeaveBalanceId = balanceId,
        EmployeeId = ids.Employee, LeaveTypeId = ids.LeaveType, LeavePeriodId = ids.LeavePeriod,
        LeaveRequestId = ids.Request, TransactionType = type, Quantity = 1m, EffectiveDate = new(2027, 1, 1),
        OccurredAtUtc = DateTime.UtcNow, LeavePolicyVersionId = ids.PolicyVersion, LeavePolicyRuleId = ids.PolicyRule,
        SourceType = LeaveBalanceSourceType.Policy, ActorType = LeaveBalanceActorType.System,
        IdempotencyKey = suffix, PayloadFingerprint = suffix.PadRight(64, '0')
    };

    private static async Task<Ids> SeedAsync(SqliteInMemoryDatabase database, decimal? granted, decimal reserved = 0m, decimal consumed = 0m)
    {
        var ids = new Ids(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var context = database.CreateContext(new TestTenantContext());
        context.Tenants.Add(new Tenant { Id = ids.Tenant, TenantCode = ids.Tenant.ToString("N")[..8], Host = ids.Tenant + ".local", ShardKey = ids.Tenant.ToString("N"), TenantName = "Test" });
        context.Employees.Add(new Employee { Id = ids.Employee, TenantId = ids.Tenant, FirstName = "Test", LastName = "Employee", Email = ids.Employee + "@test.local" });
        context.LeaveTypes.Add(new LeaveType { Id = ids.LeaveType, TenantId = ids.Tenant, Code = "AL", Name = "Annual Leave" });
        context.LeavePeriods.Add(new LeavePeriod { Id = ids.LeavePeriod, TenantId = ids.Tenant, Code = "2027", Name = "2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) });
        context.LeavePolicies.Add(new LeavePolicy { Id = ids.Policy, TenantId = ids.Tenant, Code = "POL", Name = "Policy" });
        context.LeavePolicyVersions.Add(new LeavePolicyVersion { Id = ids.PolicyVersion, TenantId = ids.Tenant, LeavePolicyId = ids.Policy, VersionNumber = 1, EffectiveFrom = new(2027, 1, 1), Status = LeavePolicyVersionStatus.Published });
        context.LeavePolicyRules.Add(new LeavePolicyRule { Id = ids.PolicyRule, TenantId = ids.Tenant, LeavePolicyVersionId = ids.PolicyVersion, LeaveTypeId = ids.LeaveType });
        context.EmployeeEmploymentHistory.Add(new EmployeeEmploymentHistory { Id = ids.Employment, TenantId = ids.Tenant, EmployeeId = ids.Employee, EffectiveFrom = new(2026, 1, 1) });
        context.LeaveRequests.Add(new LeaveRequest { Id = ids.Request, TenantId = ids.Tenant, EmployeeId = ids.Employee, LeaveTypeId = ids.LeaveType, LeavePeriodId = ids.LeavePeriod, LeavePolicyVersionId = ids.PolicyVersion, LeavePolicyRuleId = ids.PolicyRule, EmployeeEmploymentHistoryId = ids.Employment, StartDate = new(2027, 1, 2), EndDate = new(2027, 1, 2), RequestedQuantity = 1, ChargeableQuantity = 1, IdempotencyKey = ids.Request.ToString("N"), PayloadFingerprint = new string('a', 64) });
        if (granted is decimal value)
            context.EmployeeLeaveBalances.Add(new EmployeeLeaveBalance { Id = Guid.NewGuid(), TenantId = ids.Tenant, EmployeeId = ids.Employee, LeaveTypeId = ids.LeaveType, LeavePeriodId = ids.LeavePeriod, GrantedQuantity = value, ReservedQuantity = reserved, ConsumedQuantity = consumed });
        await context.SaveChangesAsync();
        return ids;
    }

    private sealed record Ids(Guid Tenant, Guid Employee, Guid LeaveType, Guid LeavePeriod, Guid Policy, Guid PolicyVersion, Guid PolicyRule, Guid Employment, Guid Request);
}
