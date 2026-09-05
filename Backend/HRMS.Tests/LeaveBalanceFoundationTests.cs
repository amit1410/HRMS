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

public sealed class LeaveBalanceFoundationTests
{
    [Fact]
    public async Task First_credit_creates_balance_and_available_is_derived()
    {
        using var db = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(db);
        using var context = db.CreateContext(new TestTenantContext(ids.Tenant));
        var poster = new LeaveBalanceTransactionPoster(context, new TestTenantContext(ids.Tenant), TimeProvider.System);

        var result = await poster.PostCreditAsync(Command(ids, LeaveBalanceTransactionType.Opening, 5m, "opening-1"));

        Assert.True(result.Succeeded);
        Assert.Equal(5m, result.Value!.GrantedQuantity);
        Assert.Equal(5m, result.Value.AvailableQuantity);
        Assert.Equal(1, context.LeaveBalanceTransactions.Count());
    }

    [Fact]
    public async Task Credits_reuse_business_balance_and_only_increase_granted()
    {
        using var db = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(db);
        using var context = db.CreateContext(new TestTenantContext(ids.Tenant));
        var poster = new LeaveBalanceTransactionPoster(context, new TestTenantContext(ids.Tenant), TimeProvider.System);

        await poster.PostCreditAsync(Command(ids, LeaveBalanceTransactionType.Opening, 5m, "opening-1"));
        var second = await poster.PostCreditAsync(Command(ids, LeaveBalanceTransactionType.Accrual, 2m, "accrual-1"));

        Assert.True(second.Succeeded);
        Assert.Equal(7m, second.Value!.GrantedQuantity);
        Assert.Equal(0m, second.Value.ReservedQuantity);
        Assert.Equal(0m, second.Value.ConsumedQuantity);
        Assert.Equal(1, context.EmployeeLeaveBalances.Count());
    }

    [Fact]
    public async Task Same_idempotency_key_replays_but_different_payload_conflicts()
    {
        using var db = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(db);
        using var context = db.CreateContext(new TestTenantContext(ids.Tenant));
        var poster = new LeaveBalanceTransactionPoster(context, new TestTenantContext(ids.Tenant), TimeProvider.System);
        var command = Command(ids, LeaveBalanceTransactionType.ExternalGrant, 3m, "grant-1", LeaveBalanceSourceType.External, "external-1");

        var first = await poster.PostCreditAsync(command);
        var replay = await poster.PostCreditAsync(command);
        var conflict = await poster.PostCreditAsync(command with { Quantity = 4m });

        Assert.True(first.Succeeded);
        Assert.True(replay.Succeeded);
        Assert.Equal(first.Value!.TransactionId, replay.Value!.TransactionId);
        Assert.Equal(ResultStatus.Conflict, conflict.Status);
        Assert.Equal(3m, (await context.EmployeeLeaveBalances.SingleAsync()).GrantedQuantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_quantity_is_rejected(decimal quantity)
    {
        using var db = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(db);
        using var context = db.CreateContext(new TestTenantContext(ids.Tenant));
        var poster = new LeaveBalanceTransactionPoster(context, new TestTenantContext(ids.Tenant), TimeProvider.System);

        var result = await poster.PostCreditAsync(Command(ids, LeaveBalanceTransactionType.Opening, quantity, Guid.NewGuid().ToString("N")));

        Assert.Equal(ResultStatus.ValidationFailed, result.Status);
    }

    [Fact]
    public async Task Cross_tenant_command_is_rejected_and_missing_balance_is_not_unlimited()
    {
        using var db = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(db);
        var otherTenant = Guid.NewGuid();
        using var context = db.CreateContext(new TestTenantContext(ids.Tenant));
        var poster = new LeaveBalanceTransactionPoster(context, new TestTenantContext(ids.Tenant), TimeProvider.System);

        var rejected = await poster.PostCreditAsync(Command(ids with { Tenant = otherTenant }, LeaveBalanceTransactionType.Opening, 1m, "cross-tenant"));
        var read = await new LeaveBalanceReader(context, new TestTenantContext(ids.Tenant)).GetAsync(ids.Tenant, ids.Employee, ids.LeaveType, ids.LeavePeriod);

        Assert.Equal(ResultStatus.Unauthorized, rejected.Status);
        Assert.Equal(ResultStatus.NotFound, read.Status);
    }

    [Fact]
    public async Task Ledger_transactions_are_append_only()
    {
        using var db = new SqliteInMemoryDatabase();
        var ids = await SeedAsync(db);
        using var context = db.CreateContext(new TestTenantContext(ids.Tenant));
        var poster = new LeaveBalanceTransactionPoster(context, new TestTenantContext(ids.Tenant), TimeProvider.System);
        var posted = await poster.PostCreditAsync(Command(ids, LeaveBalanceTransactionType.Opening, 1m, "immutable-1"));
        var transaction = await context.LeaveBalanceTransactions.SingleAsync();
        transaction.Quantity = 2m;

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.True(posted.Succeeded);
    }

    [Fact]
    public void Balance_and_ledger_model_have_tenant_filters_keys_precision_and_checks()
    {
        using var db = new SqliteInMemoryDatabase();
        using var context = db.CreateContext(new TestTenantContext(Guid.NewGuid()));
        var balance = context.Model.FindEntityType(typeof(EmployeeLeaveBalance))!;
        var ledger = context.Model.FindEntityType(typeof(LeaveBalanceTransaction))!;

        Assert.NotNull(balance.GetQueryFilter());
        Assert.NotNull(ledger.GetQueryFilter());
        Assert.Contains(balance.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "EmployeeId", "LeaveTypeId", "LeavePeriodId"]));
        Assert.Contains(ledger.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "IdempotencyKey"]));
        Assert.Equal(9, balance.FindProperty(nameof(EmployeeLeaveBalance.GrantedQuantity))!.GetPrecision());
        Assert.Equal(3, balance.FindProperty(nameof(EmployeeLeaveBalance.GrantedQuantity))!.GetScale());
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var designBalance = designModel.FindEntityType(typeof(EmployeeLeaveBalance))!;
        var designLedger = designModel.FindEntityType(typeof(LeaveBalanceTransaction))!;
        Assert.Contains(designBalance.GetCheckConstraints(), x => x.Name == "CK_EmployeeLeaveBalances_NonNegativeAndAvailable");
        Assert.Contains(designLedger.GetCheckConstraints(), x => x.Name == "CK_LeaveBalanceTransactions_PositiveQuantity");
        Assert.True(balance.FindProperty(nameof(EmployeeLeaveBalance.RowVersion))!.IsConcurrencyToken);
    }

    private static LeaveBalanceCreditCommand Command(
        Ids ids,
        LeaveBalanceTransactionType transactionType,
        decimal quantity,
        string idempotencyKey,
        LeaveBalanceSourceType sourceType = LeaveBalanceSourceType.Policy,
        string? sourceReference = null) =>
        new(ids.Tenant, ids.Employee, ids.LeaveType, ids.LeavePeriod, transactionType, quantity, new(2027, 1, 1),
            null, null, sourceType, sourceReference, LeaveBalanceActorType.System, null, null, idempotencyKey, null);

    private static async Task<Ids> SeedAsync(SqliteInMemoryDatabase db)
    {
        var ids = new Ids(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        using var context = db.CreateContext(new TestTenantContext());
        context.Tenants.Add(new Tenant { Id = ids.Tenant, TenantCode = ids.Tenant.ToString("N")[..8], Host = ids.Tenant + ".local", ShardKey = ids.Tenant.ToString("N"), TenantName = "Test" });
        context.Employees.Add(new Employee { Id = ids.Employee, TenantId = ids.Tenant, FirstName = "Test", LastName = "Employee", Email = ids.Employee + "@test.local" });
        context.LeaveTypes.Add(new LeaveType { Id = ids.LeaveType, TenantId = ids.Tenant, Code = "AL", Name = "Annual Leave" });
        context.LeavePeriods.Add(new LeavePeriod { Id = ids.LeavePeriod, TenantId = ids.Tenant, Code = "2027", Name = "2027", StartDate = new(2027, 1, 1), EndDate = new(2027, 12, 31) });
        await context.SaveChangesAsync();
        return ids;
    }

    private sealed record Ids(Guid Tenant, Guid Employee, Guid LeaveType, Guid LeavePeriod);
}
